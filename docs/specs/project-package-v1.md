# `.logiclab` Project Package V1

> Status: normative native-carrier contract

`.logiclab` is the only native V1 carrier. It is a ZIP container decoded by the Project Format Module. ZIP bytes are transport, not Project identity.

## Module interface

Project Format is an asynchronous-I/O deep Module with exactly two entry points:

```text
ReadAsync(ProjectPackageReadRequest, CancellationToken)
  -> Task<PackageReadSucceeded | PackageReadRejected>

WriteAsync(ProjectPackageWriteRequest, CancellationToken)
  -> Task<PackageWriteSucceeded | PackageWriteRejected>

ProjectPackageReadRequest
  source: readable Stream positioned at the first carrier byte
  PackagePolicy

ProjectPackageWriteRequest
  ProjectRevision
  destination: writable Stream
  PackagePolicy
```

`PackageReadSucceeded` contains one complete Import Candidate, Project content digest, package digest, and Package Evidence. `PackageReadRejected` contains one exact Project Format reason, ordered Diagnostics, and Package Evidence. `PackageWriteSucceeded` contains the source Project Revision ID, Project content digest, package digest, `carrierByteCount: UnsignedDecimal`, and Package Evidence. `PackageWriteRejected` contains one exact Project Format reason, ordered Diagnostics, and Package Evidence. Package Evidence has this exact form:

```text
PackageEvidence
  policy: { policyId: StableToken, policyRevision: StableToken }
  observedDimensions: PackageDimensionObservationV1[]
  policyLimitBreach: PackageDimensionObservationV1 | null

PackageDimensionObservationV1
  dimension: one PackagePolicy dimension
  observed: UnsignedDecimal
```

`observedDimensions` has at most one row per dimension, records the maximum observed before termination, and follows Package Policy dimension order. `policyLimitBreach` is present exactly for `package_limit_exceeded` and matches one observation. Evidence never contains raw project content or host paths.

The caller owns both streams; Project Format never closes them, retains them, or assumes a filesystem path. Read consumes one carrier from the supplied position and treats all bytes and stream behavior as untrusted. Write begins at the destination's current position. If Write is cancelled or rejected, bytes already written may remain, so the caller must provide an unpublished staging stream and expose or copy it only after `PackageWriteSucceeded`. A destination failure is an infrastructure outcome, not a malformed Project outcome.

The Package Policy is captured once and applies symmetrically: the writer never emits a carrier that the same Project Format build and policy would reject for a size or shape dimension. Calls own their parser, builder, hash, compression, and temporary resources and may run concurrently. Cancellation observed before the terminal result publishes no Import Candidate or successful carrier; Project Format performs no hidden retry and creates no background queue.

## 1. Logical contents

```text
manifest.json
project.json
memory/<memory-image-id>.bin   zero or more
```

V1 permits no other entries, directory entries, alternate casing, or opaque extensions. Paths are ASCII, use `/`, and are compared ordinally after validation. `.` and `..` segments, empty segments, leading `/`, backslash, colon, NUL, and Unicode look-alikes are rejected.

Transaction History, Simulation Session state, runtime RAM, Trace, Compilation artifacts, thumbnails, and authentication data are never included.

## 2. Manifest

`manifest.json` is strict UTF-8 JSON with this logical shape:

```json
{
  "format": "logiclab",
  "schemaVersion": 1,
  "projectPart": {
    "path": "project.json",
    "length": 0,
    "sha256": "lowercase-hex"
  },
  "memoryParts": [
    {
      "memoryImageId": "opaque-id",
      "path": "memory/opaque-id.bin",
      "length": 0,
      "sha256": "lowercase-hex"
    }
  ],
  "packageDigest": "lowercase-hex"
}
```

`length` is the uncompressed byte length encoded as a non-negative JSON integer within unsigned 64-bit range. Memory parts are sorted by `memoryImageId` and path. IDs are opaque canonical strings; consumers do not parse time or authorization from them.

Every shown member is required; duplicate or unknown members are invalid. `format` is exactly `logiclab`, `schemaVersion` is exactly `1`, and `projectPart.path` is exactly `project.json`. Every `sha256` and `packageDigest` value is exactly 64 lowercase hexadecimal characters. A `memoryImageId` uses the `OpaqueIdV1` syntax from [Project Document JSON V1](./project-document-json-v1.md), and its path is exactly `memory/{memoryImageId}.bin`; aliases, duplicate IDs, and shared parts are invalid.

The SHA-256 of a part is over its exact uncompressed bytes. The package digest excludes `manifest.json` to avoid self-reference and is SHA-256 over:

```text
UTF8("logiclab-package-v1\0")
for each declared non-manifest part in ordinal path order:
    UInt32LE(path UTF-8 byte length)
    path UTF-8 bytes
    UInt64LE(uncompressed content length)
    32 raw SHA-256 bytes
```

The package digest proves internal consistency, not authorship or trust. A Project Revision ID is never derived from it.

## 3. Project document JSON

The complete record, discriminator, identity, ordering, Unicode, and canonical-byte contract is [Project Document JSON V1](./project-document-json-v1.md). That specification is the sole owner of nested DTO shape; Project Format does not infer topology or Component parameters from JSON object structure.

The Project content digest uses the section 2 framed part sequence with domain prefix `UTF8("logiclab-project-content-v1\0")`. The `project.json` length and hash come from its canonical bytes; each memory length and hash comes from the validated canonical header/payload bytes; paths use ordinal order. The digest compares normalized content; it is not authored Project identity, Project Revision identity, Durable Project identity, authorship, or authorization.

`System.Text.Json` source-generated contexts and explicit converters are appropriate implementations. The .NET 10 serializer options set `AllowDuplicateProperties = false`. Project Format also performs a bounded `Utf8JsonReader` validation pass before typed deserialization so low-level reader and custom-converter paths cannot bypass duplicate-member, depth, token, or numeric checks.

## 4. Memory image encoding

Each memory part begins with this little-endian header:

| Bytes | Field      | Value                                 |
| ----: | ---------- | ------------------------------------- |
|     4 | magic      | ASCII `LLMI`                          |
|     2 | version    | unsigned `1`                          |
|     1 | encoding   | unsigned `1` for packed two-bit logic |
|     1 | reserved   | zero                                  |
|     4 | word width | positive unsigned bits per word       |
|     8 | depth      | positive unsigned word count          |

Payload cells are flattened by `cellIndex = address * wordWidth + bitIndex`, with bit index zero least significant. Four cells are packed per byte from least-significant to most-significant two-bit field:

| Code | Value            |
| ---: | ---------------- |
| `00` | `0`              |
| `01` | `1`              |
| `10` | `X`              |
| `11` | reserved; reject |

Payload length is exactly `ceil(wordWidth * depth * 2 / 8)`. Unused high fields in the final byte are zero. All multiplication, addition, and conversion is checked before allocation or read. `Z` cannot be stored in an authored memory image.

## 5. ZIP profile

- accepted compression methods are Store and Deflate;
- encrypted entries and unsupported features are rejected;
- ZIP64 structures are accepted only when every actual and declared limit remains within the active Package Policy;
- duplicate entry names are detected by enumerating the entire central directory; `GetEntry` is not duplicate detection;
- declared compressed and uncompressed sizes are hints, never trusted limits;
- every declared part is streamed, counted, and hashed while reading;
- no entry is extracted to a caller-controlled path and `ExtractToDirectory` is never used;
- a non-seekable upload is spooled to a bounded application-generated temporary file before ZIP inspection;
- partial temporary data is deleted on completion or failure through a recoverable cleanup path.

Export uses canonical entry order and metadata where practical, but compressed ZIP bytes are not required to be reproducible across runtime versions. Identity rests on canonical uncompressed parts and framed digests.

## 6. Read pipeline and Application handoff

```text
bounded upload stream
  -> bounded seekable spool
  -> enumerate and validate every ZIP entry
  -> read and validate manifest
  -> stream, count, and hash every declared part
  -> validate strict JSON and memory headers/payloads
  -> require the exact V1 DTO schema version
  -> translate through Circuit Authoring-owned constructors into a complete Project Document candidate
  -> return Import Candidate to Application
```

Project Format owns carrier, bounded decoding, strict V1 DTO, and integrity validation. It uses the Circuit Authoring invariant implementation exposed by Domain and does not maintain a second semantic validator. It neither invokes Project Editor or Compiler nor creates a Project Revision, Workspace, or Durable Project.

Application passes a successful Import Candidate to [`OpenAsync(ImportProject)`](../contracts/editor-workspace.md#3-editor-workspace-interface), which owns Project Genesis, Compilation, and atomic Workspace publication. Import preserves authored Project ID but allocates new Workspace and Project Revision identities; any later Durable Project receives its own Durable Project ID.

## 7. Export and browser transfer

Project Format writes to a caller-owned stream and never needs a filesystem path. Application returns `ExportPrepared` only after `PackageWriteSucceeded`; Web maps its opaque Export Ticket to the download route. [Editor Workspace](../contracts/editor-workspace.md) owns preparation, while [HTTP Boundary](../contracts/http-boundary.md) owns download authorization, URL mapping, streaming, filename, and antiforgery behavior.

Uploads declare an explicit maximum at the browser and ASP.NET Core stream boundary. Client filename, MIME type, claimed length, ZIP metadata, IDs, and JSON are untrusted. Cookie-authenticated HTTP mutations retain antiforgery protection.

## 8. Failure classes

| Class          | Examples                                                          | Publication |
| -------------- | ----------------------------------------------------------------- | ----------- |
| Carrier        | malformed ZIP, duplicate or illegal path, unsupported compression | none        |
| Limit          | upload, expansion, entry, depth, token, or logical-size policy    | none        |
| Integrity      | length, part hash, package digest, memory payload mismatch        | none        |
| Schema         | duplicate/unknown JSON member, discriminator, version, number     | none        |
| Domain         | duplicate ID, bad Terminal, width, topology, missing reference    | none        |
| Infrastructure | spool or storage failure, cancellation, shutdown                  | none        |

Diagnostics use the Project Format catalog in [Diagnostics V1](./diagnostics-v1.md). They do not echo raw project content, paths beyond validated logical names, stack traces, or host filesystem details.

## 9. Required evidence

- golden V1 packages and canonical Project content digests;
- all nested DTO and canonical-byte evidence required by [Project Document JSON V1](./project-document-json-v1.md);
- strict JSON tests for every member, discriminator, number, ordering, and migration;
- memory header, endianness, tail-field, overflow, and round-trip properties;
- duplicate path, traversal, case, Unicode, ZIP64, encrypted, truncated, and zip-bomb corpus;
- read-count enforcement against false ZIP metadata;
- cancellation and failure at every read/write pipeline phase with no successful carrier or Import Candidate;
- Application handoff integration proving Genesis or Compilation failure publishes no Workspace or durable state;
- import-export-import semantic equivalence;
- authorization, antiforgery, filename, and one-time download tests.

Security guidance: [OWASP File Upload Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html), [Microsoft Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads?view=aspnetcore-10.0), [System.IO.Compression](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression?view=net-10.0), and [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/api/system.text.json?view=net-10.0).
