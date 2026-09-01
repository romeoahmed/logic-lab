# Compiler

> Status: normative V1 translation and publication contract

Compiler validates one immutable Project Revision, elaborates hierarchy, and publishes purpose-specific artifacts with complete source provenance. It is a synchronous, deterministic, CPU-bound deep Module. Callers cannot construct IR, choose passes, or observe mutable builders.

## 1. Interface, outcomes, and evidence

[Architecture](../architecture.md#module-catalog) fixes the Compiler seam; this specification owns its exact interface, closed outcomes, translation, evidence, and publication behavior.

The synchronous CPU-bound Module exposes exactly one V1 entry point:

```text
Compile(CompilationRequest, CancellationToken)
  -> CompilationSucceeded | CompilationRejected

CompilationRequest
  ProjectRevision
  entryCircuitDefinitionId
  resolved LibrarySnapshot
  ProjectScalePolicy

```

`CompilationSucceeded` contains one sealed Compilation Artifact, its key, ordered
Diagnostics, and Compilation Evidence. `CompilationRejected` contains one exact
reason, ordered Diagnostics, and Compilation Evidence. Collections are present and
non-null.

The resolved Library Snapshot is an immutable Domain-owned value whose fingerprint must match the Project Revision references; Compiler never resolves a live registry implicitly. The Project Scale Policy is an admission/work envelope, not an algorithm selector. A caller cannot supply passes, caches, ordinal seeds, prior artifacts, or partially resolved contracts.

Compile may be called concurrently for different immutable requests. A call owns
all mutable builders and publishes no shared mutable state. Cancellation is
cooperative: a cancellation observed before publication returns the typed cancelled
rejection; a signal arriving after atomic publication does not revoke the published
success. The method is deliberately not async—Application schedules it on its typed
CPU lane and never wraps it in hidden per-call `Task.Run` work.

Compilation rejection reasons use the Compiler registry in [Diagnostics V1 §11](./diagnostics-v1.md#11-outcome-reason-registry). Expected circuit mistakes use `compilation_invalid`; exceptions and partial artifacts never cross the seam.

`ProjectScalePolicy` follows [Policies](../policies.md), controls bounded work and allocation, and never changes circuit meaning or a successful artifact.

```text
CompilationEvidence
  requestedProjectRevisionId
  requestedEntryCircuitDefinitionId
  librarySnapshotFingerprint: Digest
  compilerSemanticVersion: StableToken
  policy: { policyId: StableToken, policyRevision: StableToken }
  observedDimensions: ObservedDimensionV1[]
  policyLimitBreach: ObservedDimensionV1 | null

ObservedDimensionV1
  dimension: StableToken
  observed: UnsignedDecimal
```

All fields and arrays are present and non-null except `policyLimitBreach`.
`observedDimensions` has at most one row per dimension, records the maximum work
observed before termination, and is ordinally sorted by `dimension`; an attempt
that terminates before measuring work uses an empty array.

Every Compilation outcome carries `CompilationEvidence`. `policyLimitBreach` is
present exactly for `compilation_policy_exhausted`, matches the corresponding
observed-dimension row, and is absent for success, invalid input, cancellation,
infrastructure failure, and internal defect. `Digest`, `StableToken`, and
`UnsignedDecimal` use [Diagnostics V1](./diagnostics-v1.md#2-diagnostic-record)
lexical forms.

## 2. Inputs and provenance

`CompilationArtifactKey` contains exactly:

```text
ProjectRevisionId
EntryCircuitDefinitionId
LibrarySnapshotFingerprint
CompilerSemanticVersion
```

`LibrarySnapshotFingerprint` is the canonical digest of the ordinally ordered exact library IDs, versions, and content digests resolved for the Project Revision. It is not a package, cache, or durable identity.

The key excludes Workspace identity, Compilation Generation, Project Scale Policy, cancellation, cache identity, process identity, and runtime layout addresses. Policy can reject work but cannot select a different successful meaning. Workspace owns newest-wins Compilation Generation; Compiler sees one attempt.

The `logiclab.core` Library Snapshot schema, Contract Keys, generated Ports, normalized parameters, state shapes, and semantic digests have one implementation owner in `LogicLab.Domain`. Compiler consumes that owned schema and supplies purpose-specific evaluator lowering. Diagram Presentation supplies symbol lowering separately.

## 3. Validation and elaboration

Compilation performs these logical stages in stable order:

1. validate the exact Project Revision, entry definition, Library Snapshot, and policy provenance;
2. resolve Component Contracts and parameters, then measure generated Port shapes without generating Port identities;
3. build the definition call graph and reject recursion with one canonical witness;
4. elaborate every reachable occurrence with a complete Hierarchy Path;
5. admit the complete elaborated shape under Project Scale Policy;
6. materialize generated Ports and resolve Memory Images into contiguous packed two-plane
   storage, after admitting each referenced image's checked `word width * depth` bit-cell count,
   and resolve Circuit Definition call sites;
7. validate directions, widths, Driver rules, state schemas, and memory shapes;
8. construct the evaluator/Driver/Net graph and cut state outputs;
9. compute combinational strongly connected components and the condensation order;
10. assign dense ordinals only after canonical semantic order is fixed;
11. build Simulation IR and a total Source Map; and
12. validate and publish one sealed Compilation Artifact atomically.

An unconnected receiving Terminal reports `compiler_required_terminal_unconnected` and rejects Compilation. Receiving Terminals are Component input Ports and Circuit Definition output Ports within their containing definition. Driving Terminals may remain unconnected. A Net with no effective Driver remains executable and resolves to `Z` with Runtime evidence; multiple Drivers are legal and use four-state resolution.

Project Editor owns local Project Document invariants. Compiler rechecks every fact it relies on and owns graph-wide diagnostics; it does not repair topology, choose authored identity, or infer connectivity from coordinates.

## 4. Purpose-specific representations

Compiler uses distinct immutable representations:

| Representation   | Retains                                                                                      | Excludes                                                       |
| ---------------- | -------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| Elaborated Graph | resolved occurrences, Hierarchy Paths, widths, contracts, Driver facts, diagnostic witnesses | Session state, dense storage commitments, geometry             |
| Simulation IR    | dense ordinals, evaluator/Net graph, CSR adjacency, SCC plan, state and memory schema        | authored edit structure, browser identity, analysis algorithms |
| Source Map       | total ordinal-to-source mapping and Hierarchy Paths                                          | localized messages, renderer identity                          |

The Elaborated Graph is Compiler implementation retained only as needed to build
the sealed Simulation artifact and Source Map. Compilation does not materialize
an optional Boolean Region.

IR is never serialized, persisted, accepted from a caller, sent to the browser, or used as authored identity. A diagnostic S-expression or text dump may be added only as a derived test/debug projection; it is not a format, interface, cache key, or executable input.

## 5. Simulation IR and Source Map invariants

- Ordinals are dense, zero-based, Compilation-local, and assigned from canonical source order.
- Every evaluator input, Driver, Net, fanout, SCC member, state slot, and memory range is in bounds.
- CSR offsets are monotonic, start at zero, and end at the exact backing-array length.
- Each combinational node belongs to exactly one SCC; the condensation order covers every SCC once.
- Every externally observable diagnostic, Probe binding, Trace value, and
  state-migration fact maps to stable source identity and a complete Hierarchy Path.
- Mutable builders, spare pooled capacity, writable arrays, spans, owners, hash enumeration, and implementation object identity never cross the seam.

A private packed representation must remain observationally equivalent to the scalar semantics. Record types do not make referenced arrays immutable; artifact construction must transfer or copy exact-sized owned storage and expose no writable alias.

## 6. Determinism, policy, and publication

- Diagnostics follow [Diagnostics V1](./diagnostics-v1.md) and use Source Map locations before crossing the seam.
- Stable source order breaks otherwise equal traversal and work ties; dictionary order, task completion, allocation, culture, and localized text are never observable.
- Checked policy admission occurs before large allocation and at every expanding worklist or table.
- A policy failure reports policy ID/revision, dimension, and observed work; it does not expose fleet capacity.
- Invalid, cancelled, exhausted, or defective work publishes no artifact, cache
  entry, or partial Source Map.
- Full Compilation is the V1 semantic oracle. Incremental reuse remains internal and is allowed only after full-versus-incremental differential evidence.

Compiler does not execute user code, reflection-selected types, scripts, plug-ins, native libraries, external solvers, or S-expressions.

## 7. Required evidence

- one conformance case for every stage and rejection reason;
- recursive hierarchy, unresolved contract/Port, invalid direction, unconnected receiving Terminal, width, Driver, state, and memory matrices;
- deterministic diagnostics and artifacts under collection, hash, and traversal-order permutations;
- Source Map totality and round-trip source binding for every observable ordinal family;
- SCC membership, condensation, CSR, dense-ordinal, and bounds properties;
- scalar-versus-packed differential tests at word and tail boundaries;
- policy and cancellation injection before every publication point with no partial result;
- full Compilation versus any future incremental path, including diagnostics, Source Map, ordinals, and executable behavior; and
- negative tests proving IR dumps, runtime ordinals, and mutable storage cannot cross Module seams.

## 8. Sources

- [Compiler Representations Research](../research/compiler-representations.md)
- [Simulation Runtime](./simulation-runtime.md)
- [Component Contract Catalog V1](./component-contract-catalog-v1.md)
- [Policies](../policies.md)
- Robert Tarjan, [Depth-First Search and Linear Graph Algorithms](https://doi.org/10.1137/0201010)
