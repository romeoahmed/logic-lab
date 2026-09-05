# Circuit Authoring

> Status: normative V1 authoring contract

Project Editor owns valid Project Documents and atomic edits. Compilation,
Simulation, Workspace history, persistence, and browser gestures have separate owners.

## 1. Project Editor interface

```text
Begin(ProjectSeed) -> ProjectGenesisCommitted | ProjectGenesisRejected
Apply(ProjectRevision, EditIntent) -> EditCommitted | EditRejected
```

`NewProjectSeed` supplies the Project display name, exact Library Snapshot, Symbol
Profile, and entry Circuit Definition display name. `Begin` allocates Project,
Project Revision, and entry Circuit Definition IDs; all other collections are empty.
`ImportedProjectSeed` carries the validated Project Document from a Project Format
Import Candidate. `Begin` preserves its authored IDs and allocates a Project Revision
ID, trusting that validation boundary. Neither path saves, compiles, authorizes,
publishes a Workspace, or changes existing history.

A committed result contains one new whole-Project Revision, canonical changed and
removed source identities, and stable diagnostics. Identity sets describe the final
committed state and are disjoint. A rejected result has reason `authoring_invalid`,
diagnostics, and no revision or document changes. Application handles cancellation
and infrastructure failures separately from authoring validation.

One closed `EditIntent` expresses one user intention, possibly spanning several
entities or Circuit Definitions. All affected definitions commit together or none
do. Project Editor hides ID allocation, structural sharing, topology normalization,
and validation phases. Selection, viewport, pointer samples, history cursor,
Compilation, and Session state do not enter this interface.

## 2. Identity and ordering

- `ProjectId` survives revisions; every commit receives a new `ProjectRevisionId`.
  Neither is a content digest.
- `CircuitDefinitionId` is project-wide. Component Instance, Net, Junction, and Wire
  Geometry IDs are local to their Circuit Definition; Port IDs are local to their
  Component Contract or Circuit Definition.
- References include the containment and Hierarchy Path needed to distinguish local
  IDs. Names, coordinates, array indexes, and runtime ordinals are never identity.
- Project Editor allocates creation IDs and returns them as changed source identities.
  Only validated import preserves supplied IDs; ordinary intents cannot choose them.
- Ordered Ports, Terminal memberships, slices, and routes retain authored order.
  Map-like collections and diagnostics use canonical order, never hash enumeration.

## 3. Project Document invariants

Every revision satisfies these local invariants:

- IDs are unique in their scope; references resolve to the required entity kind,
  including the entry Circuit Definition.
- Widths are positive checked values. A connected Terminal has its Port's width and
  belongs to at most one Net; an unconnected Terminal is valid.
- A Net has one fixed width, ordered Terminal and Junction memberships, and at least
  one Terminal, Junction, or Wire Geometry. Each Junction and Wire Geometry names
  exactly one Net in the same Circuit Definition.
- Wire Geometry is an orthogonal route or explicit unrouted marker. Geometry never
  defines connectivity: crossings create no Junction and movement changes no membership.
- Each Component Instance names one exact Component Contract or Circuit Definition
  and supplies parameters valid for its local schema.
- Display text is nonempty NFC Unicode without NUL, isolated surrogates, or C0 controls.
  Annotation text may be empty and permits LF, but otherwise follows the same rules.
- Grid coordinates fit signed 32-bit integers. Memory Image width and depth are
  positive checked values, with exactly one complete word per address.
- Symbol Profile and explicit Symbol Variant references resolve exactly and preserve
  the Component Contract Port schema.

Project Editor rejects impossible local values. Compiler owns hierarchy recursion,
elaborated Driver rules, and other graph-wide errors; a locally valid revision may
remain non-executable.

## 4. Structural edits

The following V1 catalog is closed. References include their containing Circuit
Definition. Each intent supplies complete replacement values for the facts it changes.
There is no generic patch, entity update, cascade flag, nested intent list, or
caller-supplied persistent ID. Dependents must migrate through a dedicated intent
or the edit is rejected; Port and state changes never silently rebind or truncate data.

### 4.1 Circuit Definitions

| Intent | Input and atomic effect |
| --- | --- |
| `CreateCircuitDefinition` | Display name, ordered public Ports, and initial presentation create an unreferenced definition. |
| `RenameCircuitDefinition` | Changes the identified definition's display name, preserving identity and call sites. |
| `ChangePublicPortContract` | Retained Port IDs, new declarations without IDs, and complete call-site migrations replace the contract and every call site together. |
| `MoveDefinitionPorts` | Nonempty Port IDs and final placements change presentation only. |
| `SetEntryCircuitDefinition` | An existing definition ID replaces the entry reference. |
| `RemoveCircuitDefinition` | Removes a non-entry definition only when no Component Instance references it. |

Retained Ports preserve direction and width. New Ports receive new IDs; array
positions never preserve identity. Every call site must map **every old Port** to a
distinct compatible destination in the new contract or explicitly disconnect it,
including Ports that are currently unconnected. Removed definition-boundary Ports
are disconnected in the same transaction.

### 4.2 Component Instances

| Intent | Input and atomic effect |
| --- | --- |
| `PlaceComponentInstance` | Exact target, complete parameters, and placement create a locally valid instance. |
| `PlaceComponentWithNewMemoryImage` | Exact library target, non-memory parameters, a complete new Memory Image binding, and placement create both image and instance. |
| `RenameComponentInstance` | Sets display name or null, preserving target, Ports, and state. |
| `SetInstanceParameters` | Replaces complete parameters only when resolved Port and state schemas stay identical. |
| `ChangeInstanceContract` | Exact target, complete parameters, Terminal migration, and Symbol Variant replace the contract, connections, and initial state together. |
| `MoveComponentInstances` | Nonempty instance IDs and final placements change presentation only. |
| `RemoveComponentInstances` | Removes the specified nonempty instance set and its Terminal memberships; a Net is removed only if no Terminal, Junction, or Wire Geometry remains. |

Parameter bindings follow [Component Contract Catalog V1](./component-contract-catalog-v1.md).
Contract migrations map every old Port to a distinct compatible new Port or an
explicit disconnection; incompatible state and Symbol Variants cannot survive by
coincidence. `PlaceComponentWithNewMemoryImage` names the memory-image parameter
and supplies display name, width, depth, and complete words. Project Editor allocates
both IDs and rejects the whole intent if either image or binding is invalid.

### 4.3 Connectivity and geometry

| Intent | Input and atomic effect |
| --- | --- |
| `ConnectTerminals` | Compatible Terminals, optional destination Net, new Junction declarations, route additions, and complete affected route replacements create, extend, or merge connectivity and its route. |
| `MergeNets` | An existing destination Net and nonempty source Net IDs combine complete membership, preserving only the destination ID. |
| `SplitNet` | A Net ID and complete nonempty membership partitions split the Net under Section 5's identity rule. |
| `AddJunction` | A Net ID, position, route additions, and complete affected route replacements/removals add one Junction and update geometry. |
| `RemoveJunction` | A Junction ID, resulting partitions if connectivity splits, route additions, and complete affected route replacements/removals remove the Junction and update topology. |
| `AddWireGeometry` | A Net ID and complete routed/unrouted value add a route without changing electrical membership. |
| `SetWireGeometry` | A geometry ID and complete routed/unrouted value replace that route only. |
| `RemoveWireGeometry` | Removes the identified route without changing electrical membership; rejects removal of a Net's last remaining member. |

`ConnectTerminals` requires at least two distinct electrical endpoints, counting the
destination Net. A multi-Net merge requires an explicit destination. New Junctions
have explicit positions; route crossings never imply them. Route additions have no
IDs; replacements name existing geometry. Both are closed values within the topology
intent, not nested Edit Intents.

### 4.4 Authored data and presentation

| Intent | Input and atomic effect |
| --- | --- |
| `CreateMemoryImage` | Display name, width, depth, and complete initial words create an image. |
| `ReplaceMemoryImage` | Image ID, replacement shape/content, and complete affected instance parameter migrations update the image and references together. |
| `RemoveMemoryImage` | Removes the image only when no instance references it. |
| `SetSymbolProfile` | Exact profile/version/convention and complete incompatible-override removals or replacements change the project-wide profile without fallback. |
| `SetSymbolVariant` | Instance ID and registered compatible variant or null change presentation only. |
| `CreateAnnotation` | A complete annotation without ID creates authored presentation. |
| `ChangeAnnotation` | An Annotation ID and complete replacement change authored presentation. |
| `MoveAnnotations` | Nonempty Annotation IDs and final positions change presentation only. |
| `RemoveAnnotation` | Removes the identified Annotation. |

Simulation memory writes and automated circuit replacement are not V1 authoring intents.

## 5. Net identity through connectivity changes

- Connectivity without an existing Net receives a new Net ID.
- A merge preserves the explicit destination Net ID and removes all source Net IDs.
- A split partitions every Terminal, Junction, and Wire Geometry exactly once. The
  partition with the lowest canonical Terminal reference retains the original ID.
  With no Terminals, the lowest Junction ID decides; with neither, the lowest Wire
  Geometry ID decides. IDs compare ordinally, independently of culture.
- Remaining partitions receive new IDs in canonical partition-key order. Empty
  partitions are never published.
- Splits, merges, and Junction deletion update every affected Wire Geometry reference
  in the same transaction.

Identity retention is independent of coordinates, input enumeration, and traversal.
Project Format uses these same authoring invariants to construct an Import Candidate;
Compiler rechecks the executable graph. Neither reconstructs connectivity or chooses
a different retained partition.

## 6. Revision and history behavior

`Begin` produces one Project Genesis; each successful `Apply` produces exactly one
later Project Revision and defines the smallest Undo/Redo unit. Rejection publishes
no revision. [Editor Workspace](../contracts/editor-workspace.md) owns history bases,
Undo/Redo, branch truncation, intent idempotency, and Compilation staleness.

## 7. Diagnostics and determinism

Expected authoring mistakes return structured diagnostics under
[Diagnostics V1](./diagnostics-v1.md): stable codes, typed safe arguments, source
locations, and canonical ordering. Web owns localization. Diagnostic witnesses,
identity retention, and changed/removed identity sets are independent of input map
order, process state, culture, and browser geometry.

## 8. Required evidence

- model-based Edit Intent sequences preserving every Project Document invariant;
- new/imported Genesis, including rejection before Workspace publication;
- each closed Edit Intent, generic/unknown variant rejection, and atomic commits
  across multiple definitions with no partial revision on rejection;
- merge/split permutations and culture changes proving identity retention;
- deletion, Port/schema/width migration, duplicate membership, and dangling-reference
  rejection, including shared Nets at multiple call sites;
- crossings, Junctions, unrouted geometry, and geometry-only movement;
- diagnostics and disjoint final source sets under input-order permutations;
- Workspace idempotency, Undo/Redo, branch truncation, and Compilation staleness; and
- locally valid recursive or electrically invalid revisions that remain editable
  and receive deterministic Compiler diagnostics.
