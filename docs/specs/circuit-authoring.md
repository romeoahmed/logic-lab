# Circuit Authoring

> Status: normative V1 authoring contract

Circuit Authoring defines the valid Project Document and the atomic behavior of the Project Editor Module. Compilation, Simulation, Workspace continuity, persistence, and browser gestures are separate concerns.

## 1. Project Editor interface

```text
Begin(ProjectSeed) -> ProjectGenesisCommitted | ProjectGenesisRejected
Apply(ProjectRevision, EditIntent) -> EditCommitted | EditRejected
```

`ProjectSeed` is `NewProjectSeed` or `ImportedProjectSeed`. A new seed supplies the Project display name, exact Library Snapshot, Symbol Profile, and entry Circuit Definition display name. Project Editor allocates the authored Project ID, Project Revision ID, and one empty entry Circuit Definition ID; all other collections begin empty. An imported seed carries the validated Project Document established by the Project Format Import Candidate, preserves its authored IDs, and receives a new Project Revision ID. `Begin` trusts that capability boundary; it does not repeat validation, save, compile, authorize, publish a Workspace, or mutate an existing history.

`EditIntent` is one closed variant describing one user intention. An intent may affect several entities or Circuit Definitions when that is necessary to preserve invariants; it is not an arbitrary patch or list of smaller commands.

`ProjectGenesisCommitted` or `EditCommitted` contains one new whole-Project Revision, the canonical changed and removed source identities, and stable diagnostics. A rejected result carries reason `authoring_invalid`, diagnostics, and no revision. Cancellation and infrastructure handling occur at the Application seam rather than being disguised as authoring validation. The Module hides identity allocation, structural sharing, topology normalization, and validation phases. Selection, viewport, pointer samples, Undo/Redo cursor, Compilation, and Session state never enter this interface.

## 2. Identity and ordering

- `ProjectId` is stable across Project Revisions. Every committed edit creates a new `ProjectRevisionId`; neither value is a content digest.
- `CircuitDefinitionId` is project-wide. Component Instance, Net, Junction, and Wire Geometry IDs are stable within their Circuit Definition. Port IDs are stable within their owning Component Contract or Circuit Definition.
- References always include enough containment and Hierarchy Path information to disambiguate a local ID. Display names, coordinates, array indexes, and runtime ordinals are never identity.
- Ordinary creation intents receive new persistent IDs from the Project Editor and return them in the committed result. Import retains only IDs already decoded and validated by Project Format; a browser gesture cannot choose a persistent ID.
- Ordered Ports, Terminal memberships, slices, routes, and other semantic sequences retain explicit authored order. Map-like collections and diagnostics use canonical order; hash enumeration is never observable.

## 3. Project Document invariants

Every Project Revision satisfies these local invariants even when Compiler later reports graph-wide errors:

- every ID is unique in its declared scope and every reference resolves to the required entity kind;
- widths are positive, checked values; a connected Terminal has exactly its Port width;
- a Terminal belongs to at most one Net; an unconnected Terminal is valid;
- one Net has one fixed width, owns at least one Terminal, Junction, or Wire Geometry, and explicitly owns its ordered Terminal and Junction membership;
- each Junction and Wire Geometry names exactly one Net in the same Circuit Definition;
- Wire Geometry is an orthogonal route or an explicit unrouted marker and never defines connectivity;
- a geometric crossing creates no Junction, and moving geometry alone changes no Net membership;
- each Component Instance names one exact Component Contract or Circuit Definition and has parameters valid for that local contract schema;
- display text is nonempty NFC Unicode without NUL, isolated surrogate code points, or C0 controls; Annotation text is NFC and permits LF but no other C0 control;
- authored grid coordinates fit signed 32-bit integers, and every Memory Image has positive checked width/depth with exactly one complete word per address;
- Symbol Profile and explicit Symbol Variant references resolve exactly and preserve the Component Contract Port schema; and
- the entry Circuit Definition and all directly authored references exist.

Impossible local values are rejected by Project Editor. Hierarchy recursion, whole-graph Driver rules, and other elaborated facts belong to Compiler, so a locally valid Project Revision may be non-executable.

## 4. Structural edits

The V1 Edit Intent catalog is closed. All entity references include their containing Circuit Definition. Creation variants allocate IDs inside Project Editor and return them in `changed source identities`.

### 4.1 Circuit Definitions

| Intent                      | Required facts                                                                                             | Atomic consequence                                                                                          |
| --------------------------- | ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `CreateCircuitDefinition`   | display name, ordered public Port contract, initial presentation                                           | creates one unreferenced definition                                                                         |
| `RenameCircuitDefinition`   | definition ID, new display name                                                                            | changes no identity or call site                                                                            |
| `ChangePublicPortContract`  | definition ID, retained Port IDs, new Port declarations without IDs, complete call-site Terminal migration | allocates new Port IDs and changes the definition and every selected instance call site together or rejects |
| `MoveDefinitionPorts`       | definition ID, nonempty Port IDs with final placements                                                     | changes authored presentation only                                                                          |
| `SetEntryCircuitDefinition` | existing definition ID                                                                                     | replaces the one entry reference                                                                            |
| `RemoveCircuitDefinition`   | non-entry definition ID                                                                                    | succeeds only when no Component Instance references it                                                      |

A public Port migration maps each retained old Port ID to exactly one compatible new Port ID or explicitly disconnects it. New Ports receive new IDs; a changed meaning never reuses an old ID merely because its array position matches.

### 4.2 Component Instances

| Intent                             | Required facts                                                                                         | Atomic consequence                                                                                                      |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------- |
| `PlaceComponentInstance`           | exact target, complete parameters, placement                                                           | validates the Component Contract or Circuit Definition and creates one instance                                         |
| `PlaceComponentWithNewMemoryImage` | exact library target, complete non-memory parameters, one complete new Memory Image binding, placement | validates and creates the explicit Memory Image and bound instance together                                             |
| `RenameComponentInstance`          | instance ID, display name or null                                                                      | changes no target, Port, or state fact                                                                                  |
| `SetInstanceParameters`            | instance ID, complete parameter set                                                                    | valid only when resolved Port and state schemas remain identical                                                        |
| `ChangeInstanceContract`           | instance ID, new exact target/parameters, complete Terminal migration                                  | changes target, parameters, connections, initial state, and Symbol Variant together                                     |
| `MoveComponentInstances`           | nonempty instance IDs with final placements                                                            | changes presentation only                                                                                               |
| `RemoveComponentInstances`         | nonempty instance IDs                                                                                  | removes their Terminal memberships and instances; removes a Net only if no Terminal, Junction, or Wire Geometry remains |

Parameter bindings follow the exact [Component Contract Catalog V1](./component-contract-catalog-v1.md). A contract change cannot retain incompatible state or a Symbol Variant by coincidence.

### 4.3 Connectivity and geometry

| Intent               | Required facts                                                                                                                                                        | Atomic consequence                                                                                                           |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `ConnectTerminals`   | compatible Terminals, optional existing destination Net, new Junction declarations without IDs, route additions without IDs, and complete affected route replacements | atomically creates/adds/merges connectivity and its authored route, allocating every new Net, Junction, and Wire Geometry ID |
| `MergeNets`          | destination Net ID, nonempty source Net IDs                                                                                                                           | preserves only the destination ID and combines complete membership                                                           |
| `SplitNet`           | Net ID, complete nonempty membership partitions                                                                                                                       | applies the identity rule in Section 5 and allocates remaining Net IDs                                                       |
| `AddJunction`        | Net ID, position, route additions without IDs, and complete affected route replacements/removals                                                                      | allocates one Junction and any new Wire Geometry IDs without inferring membership from coordinates                           |
| `RemoveJunction`     | Junction ID, resulting partitions if connectivity splits, route additions without IDs, and complete affected route replacements/removals                              | removes the Junction, allocates new Wire Geometry IDs, and leaves topology/geometry consistent                               |
| `AddWireGeometry`    | Net ID, complete routed/unrouted value                                                                                                                                | allocates one Wire Geometry ID without changing Net membership                                                               |
| `SetWireGeometry`    | existing geometry ID, complete routed/unrouted value                                                                                                                  | changes presentation of one Net without changing membership                                                                  |
| `RemoveWireGeometry` | geometry ID                                                                                                                                                           | removes only that route; electrical membership is unchanged                                                                  |

`ConnectTerminals` requires at least two distinct electrical endpoints when the destination Net is counted. It rejects an ambiguous multi-Net merge without an explicit destination. New Junction positions are explicit authored topology, never inferred from route crossings. `SplitNet` partitions every current Terminal, Junction, and Wire Geometry exactly once; it is not a list of cut edges. Route additions and replacements are closed values attached to the topology intention, not nested arbitrary Edit Intents.

### 4.4 Authored data and presentation

| Intent               | Required facts                                                                                       | Atomic consequence                                         |
| -------------------- | ---------------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| `CreateMemoryImage`  | display name, width, depth, complete initial words                                                   | creates one Memory Image                                   |
| `ReplaceMemoryImage` | image ID, same or new shape, complete initial words, complete affected instance parameter migrations | replaces content and every affected reference or rejects   |
| `RemoveMemoryImage`  | image ID                                                                                             | succeeds only when no instance references it               |
| `SetSymbolProfile`   | exact profile/version/convention, complete incompatible-override removals or replacements            | changes the project-wide profile without silent fallback   |
| `SetSymbolVariant`   | instance ID, registered compatible variant or null                                                   | changes presentation only                                  |
| `CreateAnnotation`   | exact annotation value without an ID                                                                 | allocates one Annotation ID and adds authored presentation |
| `ChangeAnnotation`   | annotation ID and complete replacement value                                                         | changes authored presentation only                         |
| `MoveAnnotations`    | nonempty Annotation IDs and final positions                                                          | changes authored presentation only                         |
| `RemoveAnnotation`   | annotation ID                                                                                        | removes authored presentation only                         |

Memory writes during Simulation and automated circuit replacement are not V1
authoring intents.

`PlaceComponentWithNewMemoryImage` is the one authoring convenience for a new explicit image. Its binding names the target memory-image parameter and carries the complete display name, width, depth, and words; Project Editor allocates both persistent IDs and rejects the whole intent if either the image or component binding is invalid. It is not a nested `CreateMemoryImage` plus `PlaceComponentInstance` sequence.

An edit cannot leave a dangling reference. Deleting or changing an entity with dependents either uses a dedicated intent whose complete consequence is part of this contract or is rejected; there is no generic cascade-delete flag. A Port- or state-schema change never silently rebinds or truncates authored data.

One Edit Transaction may update several Circuit Definitions, for example when changing a public Port contract and all call sites together. All affected definitions commit or none do. V1 has no `PatchProject`, generic `UpdateEntity`, cascade flag, nested intent list, or caller-supplied persistent ID.

## 5. Net identity through connectivity changes

Connectivity intents operate on stable Terminals, Nets, and Junctions. They never ask Project Editor to infer membership from screen coordinates.

- Creating connectivity with no existing Net allocates one Net ID.
- Merging Nets requires one existing destination Net. That ID survives; every source Net ID is removed.
- Splitting a Net supplies the complete resulting membership partitions. The partition containing the lowest canonical Terminal reference retains the original Net ID. If no partition contains a Terminal, the lowest Junction ID breaks the tie, followed by the lowest Wire Geometry ID.
- Other nonempty partitions receive new IDs in canonical partition-key order. A partition containing no Terminal, Junction, or Wire Geometry is not published.
- A split, merge, or Junction deletion also leaves every affected Wire Geometry reference consistent in the same Edit Transaction.

These rules make identity preservation independent of coordinate order, collection enumeration, and implementation traversal. Compiler rechecks the executable graph, while Project Format invokes the same Circuit Authoring invariant implementation when constructing an Import Candidate; neither chooses a different partition or reconstructs connectivity.

## 6. Revision and history behavior

One successful `Begin` produces the Project Genesis. One successful `Apply` produces exactly one later Project Revision and is the smallest Undo/Redo unit. Project Editor does not own Transaction History or idempotency. A Workspace created from Genesis starts history at that revision. Opening a Durable Project or copying a Workspace instead establishes its loaded or forked Project Revision as a new history base; prior private history is not transferred and Undo cannot cross that base. Workspace records later committed revisions, moves its cursor for Undo/Redo, truncates the abandoned Redo branch after a new edit, and returns a retained result for a duplicate `ClientIntentId`.

A rejected edit consumes no visible revision and changes no Project Document. Because Compilation provenance includes Project Revision, Workspace marks the current Compilation stale after every committed edit.

## 7. Diagnostics and determinism

Expected authoring mistakes return structured diagnostics rather than exceptions. Structure, codes, typed safe arguments, locations, and ordering follow [Diagnostics V1](./diagnostics-v1.md). Localized prose belongs to Web.

At the Workspace seam, replay of the same retained intent identity returns the recorded outcome. Canonical ordering, identity-retention rules, and changed-identity sets do not depend on dictionary order, process state, or browser geometry.

## 8. Required evidence

- model-based Edit Intent sequences that preserve every Project Document invariant;
- Project Genesis from new and imported seeds, including rejection before Workspace publication;
- one conformance case for every closed Edit Intent and rejection of generic or unknown variants;
- atomic multi-definition commits and rejection without a partial revision;
- merge/split permutations proving the destination and retained-partition rules;
- deletion, Port-schema, width, duplicate-membership, and dangling-reference rejection matrices;
- crossings, Junctions, unrouted geometry, and geometry-only movement cases;
- stable diagnostics under input and collection-order permutations;
- Workspace integration for idempotency, Undo/Redo, Redo truncation, and Compilation staleness;
- Compiler integration proving that locally valid but recursive or electrically invalid revisions remain editable and fail with deterministic graph-wide diagnostics.
