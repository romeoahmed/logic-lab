# Project Document JSON V1

> Status: normative nested DTO and canonicalization contract
> Part name: `project.json` in `.logiclab` schema version `1`

This specification defines every V1 Project Document transport record. [Project Package V1](./project-package-v1.md) owns the carrier, manifest, ZIP, memory parts, and package read/write pipelines. [Circuit Authoring](./circuit-authoring.md) owns Domain invariants. JSON is a strict Project Format DTO, never a serialized object graph.

## 1. Identity and import meaning

`projectId` is authored identity preserved by export and import. It is not a Durable Project locator, authorization fact, Project Revision ID, or content digest. `projectRevisionId`, history, Durable Version, Workspace identity, and runtime state are never serialized.

Importing the same package more than once may therefore create several Workspaces or Durable Projects containing the same authored `projectId`. Each import preserves authored identity, follows the Application handoff in [Project Package V1](./project-package-v1.md#6-read-pipeline-and-application-handoff), and never appends an Edit Transaction to an unrelated Project history. A durable store scopes uniqueness by its own `DurableProjectId`.

All entity IDs use `OpaqueIdV1`: 1–64 lowercase ASCII characters matching `[a-z0-9][a-z0-9_-]*`. IDs are opaque and case-sensitive; consumers derive no kind, time, order, or authority from them. Library, contract, profile, variant, and parameter names use 1–96 ASCII characters matching `[A-Za-z][A-Za-z0-9._-]*` and remain case-sensitive.

## 2. Top-level record

Properties are required and appear in this export order:

```text
ProjectDocumentDtoV1
  projectId: OpaqueIdV1
  displayName: DisplayText
  symbolProfile: SymbolProfileRefV1
  libraryReferences: LibraryReferenceV1[]
  entryCircuitDefinitionId: OpaqueIdV1
  circuitDefinitions: CircuitDefinitionDtoV1[]
  memoryImages: MemoryImageRefV1[]
```

`libraryReferences`, `circuitDefinitions`, and `memoryImages` are map-like arrays sorted by `id`. IDs are unique in their array. Exactly one entry Circuit Definition exists. Every referenced Memory Image has one manifest memory part and every manifest memory part has one reference.

```text
SymbolProfileRefV1
  id: StableName
  version: StableVersion
  indicationConvention: "negation" | "directPolarity"

LibraryReferenceV1
  id: StableName
  version: StableVersion
  digest: exactly 64 lowercase hexadecimal SHA-256 characters

MemoryImageRefV1
  id: OpaqueIdV1
  displayName: DisplayText
  wordWidth: u32 positive
  depth: u64 positive encoded as canonical decimal string
  partPath: validated logical package path
```

`StableVersion` is an opaque 1–64 ASCII token matching `[A-Za-z0-9][A-Za-z0-9._-]*`; Project Format compares it exactly and does not infer compatibility from its spelling.

Each Memory Image `partPath` is exactly `memory/{id}.bin` using that record's `id`; aliases and shared parts are invalid.

## 3. Circuit Definition

Properties are required and exported in this order:

```text
CircuitDefinitionDtoV1
  id: OpaqueIdV1
  displayName: DisplayText
  ports: DefinitionPortDtoV1[]
  componentInstances: ComponentInstanceDtoV1[]
  nets: NetDtoV1[]
  junctions: JunctionDtoV1[]
  wireGeometry: WireGeometryDtoV1[]
  presentation: CircuitPresentationDtoV1
```

`ports` preserve authored public Port order. Component Instances, Nets, Junctions, and Wire Geometry are map-like arrays sorted by ID.

```text
DefinitionPortDtoV1
  id: OpaqueIdV1
  displayName: DisplayText
  direction: "input" | "output"
  width: u32 positive
```

V1 has no implicit inout Port. Bidirectional behavior uses explicit input, output, and tri-state contracts.

## 4. Component Instances and parameters

```text
ComponentInstanceDtoV1
  id: OpaqueIdV1
  displayName: DisplayText or null
  target: ComponentTargetV1
  parameters: ParameterBindingV1[]

ComponentTargetV1 =
  { kind: "libraryContract", libraryId: StableName, contractId: StableName }
  | { kind: "circuitDefinition", circuitDefinitionId: OpaqueIdV1 }

ParameterBindingV1
  parameterId: StableName
  value: ParameterValueV1
```

Library targets resolve through exactly one top-level Library Reference and the [Component Contract Catalog V1](./component-contract-catalog-v1.md). Definition targets require an empty `parameters` array in V1. Library parameters contain every declared parameter exactly once, in catalog order; unknown, missing, duplicate, wrong-kind, and out-of-order bindings are invalid.

`ParameterValueV1` is a closed discriminated union:

```text
{ kind: "unsigned32", value: JSON integer in 0..4294967295 }
{ kind: "unsigned64", decimal: canonical unsigned decimal string }
{ kind: "enum", value: StableName }
{ kind: "logicVector", bits: nonempty string over 0, 1, X }
{ kind: "unsigned32List", values: JSON integers in 0..4294967295 }
{ kind: "sliceList", values: [{ offset: u32, length: positive u32 }] }
{ kind: "memoryImage", memoryImageId: OpaqueIdV1 }
```

Logic-vector text is most-significant bit first; the rightmost character is bit zero. Its length must equal the contract width. An unsigned decimal string is `0` or a nonzero digit followed by digits, with no sign, whitespace, separator, or leading zero. Lists preserve contract order and use checked arithmetic.

## 5. Electrical topology

```text
NetDtoV1
  id: OpaqueIdV1
  width: u32 positive
  terminals: TerminalReferenceV1[]
  junctionIds: OpaqueIdV1[]

TerminalReferenceV1 =
  { kind: "definitionPort", portId: OpaqueIdV1 }
  | { kind: "instancePort", componentInstanceId: OpaqueIdV1, portId: StableName | OpaqueIdV1 }

JunctionDtoV1
  id: OpaqueIdV1
  netId: OpaqueIdV1
  position: GridPointV1

WireGeometryDtoV1
  id: OpaqueIdV1
  netId: OpaqueIdV1
  route: WireRouteV1

WireRouteV1 =
  { kind: "unrouted" }
  | { kind: "orthogonal", points: GridPointV1[] }

GridPointV1
  x: signed 32-bit JSON integer
  y: signed 32-bit JSON integer
```

An instance Terminal's `portId` is a catalog `StableName` for a library target or the referenced definition's public Port `OpaqueIdV1` for a Circuit Definition target. Domain resolves that identity against the target; the spelling does not determine the target kind.

Terminal and Junction membership arrays preserve authored order and contain no duplicates. Every referenced entity exists in the same Circuit Definition. Each Junction's `netId` agrees with exactly one Net membership. Every Net owns at least one Terminal, Junction, or Wire Geometry. An orthogonal route has at least two points, no adjacent duplicates, and each segment changes exactly one coordinate. A route need not be used to reconstruct membership; geometric crossings never connect.

## 6. Authored presentation

Only reproducible, project-level presentation facts are serialized. Viewport, open tabs, panels, selection, hover, Transient Preview, live values, diagnostics, and browser preferences are excluded.

```text
CircuitPresentationDtoV1
  componentPlacements: ComponentPlacementV1[]
  definitionPortPlacements: DefinitionPortPlacementV1[]
  annotations: AnnotationV1[]

ComponentPlacementV1
  componentInstanceId: OpaqueIdV1
  origin: GridPointV1
  orientation: OrientationV1
  symbolVariantId: StableName or null

DefinitionPortPlacementV1
  portId: OpaqueIdV1
  position: GridPointV1
  facing: "north" | "east" | "south" | "west"

OrientationV1
  quarterTurnsClockwise: integer 0..3
  reflected: boolean

AnnotationV1
  id: OpaqueIdV1
  text: AnnotationText
  position: GridPointV1
  alignment: "start" | "center" | "end"
```

Placements are map-like arrays sorted by referenced entity ID; each Component Instance and public Port has exactly one placement. Annotations preserve authored z-order and have unique IDs. `symbolVariantId` is either null or a registered variant compatible with the active Symbol Profile and unchanged Component Contract.

## 7. Strings and JSON lexical rules

`DisplayText` is nonempty NFC Unicode without NUL, isolated surrogate code points, or other C0 controls. `AnnotationText` is NFC Unicode and may contain LF but no other C0 controls. Length limits are explicit Package Policy dimensions measured in Unicode scalar values and UTF-8 bytes. Project Format rejects package text rather than silently trimming or normalizing it; an authoring UI may normalize before constructing an Edit Intent.

All objects reject duplicate and unknown members. All properties are required unless their type explicitly includes null. JSON comments, trailing commas, non-finite numbers, exponent notation for integer fields, negative zero, and unknown enum/discriminator text are invalid. Recursion depth, tokens, string bytes, array counts, and total decoded entities are bounded before Domain allocation.

## 8. Canonical bytes and ordering

Import accepts legal member order, insignificant JSON whitespace, and any order for map-like arrays. After duplicate-ID validation, Project Format sorts map-like arrays; semantic arrays retain their input order. Project Format then emits canonical bytes for export and the Project content digest:

1. properties use the schema order in this document;
2. map-like arrays use ordinal ID order; semantic arrays retain their declared order;
3. UTF-8 has no BOM, output has no insignificant whitespace, and the file ends with one LF;
4. strings are NFC; `\"`, `\\`, `\b`, `\t`, `\n`, `\f`, and `\r` use their short escapes, remaining U+0000–U+001F use lowercase `\u00xx`, `/` is not escaped, and all other Unicode scalar values are literal UTF-8;
5. JSON integers use the shortest decimal spelling with no leading zero, exponent, or negative zero;
6. booleans and null use lowercase JSON literals; and
7. discriminators and enums use the exact text shown here.

Canonicalization is a Project Format implementation detail at its seam; Domain types do not depend on JSON property order or escaping.

## 9. Validation and publication order

Validation is deterministic:

1. carrier and actual byte limits;
2. JSON lexical shape and duplicate-member scan;
3. closed DTO schema and scalar ranges;
4. unique IDs, sorted/canonicalizable maps, and local references;
5. Component Contract and parameter resolution;
6. topology and presentation invariants;
7. Memory Image shape and part agreement;
8. canonical bytes and content digest; then
9. Project Document construction through the Circuit Authoring-owned invariant implementation.

Import diagnostics bind to a logical part path and RFC 6901 JSON Pointer where available. Project Format returns one complete Import Candidate or none; the Application handoff is owned by the [Editor Workspace contract](../contracts/editor-workspace.md#3-editor-workspace-interface). This JSON layer publishes nothing.

## 10. Required evidence

- one minimal and one fully populated canonical golden document;
- a golden record for every target and Parameter Value discriminator;
- property-order, whitespace, and map-like-array permutations with identical canonical bytes;
- duplicate/unknown/missing member, wrong-kind, invalid Unicode, integer, decimal, ID, enum, and discriminator cases;
- duplicate and dangling identity cases at every scope;
- Terminal/Junction/route contradictions and geometry-only crossing cases;
- exact Contract Key, generated Port, parameter, Memory Image, and Symbol Variant validation;
- import-export-import equality at Project Document meaning and canonical-byte levels; and
- Project Genesis tests proving authored `projectId` is distinct from `DurableProjectId`, Project Revision ID, content digest, and authorization.
