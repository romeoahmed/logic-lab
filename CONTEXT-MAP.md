# Logic Lab Context Map

Logic Lab is a modular monolith. A bounded context owns language and consistency; it does not imply a process, database, or network service.

## Contexts

| Context                                                         | Consistency scope                     | Owns                                                                              | Excludes                                              |
| --------------------------------------------------------------- | ------------------------------------- | --------------------------------------------------------------------------------- | ----------------------------------------------------- |
| [Circuit Authoring](./docs/domain/circuit-authoring/CONTEXT.md) | one Project Revision                  | authored topology, stable identity, initial data, Edit Transactions               | compiled indexes, runtime values, file bytes          |
| [Simulation](./docs/domain/simulation/CONTEXT.md)               | one committed Quiescent Boundary      | four-state execution, Logical Time, sequential state, Trace                       | Project Document mutation, Undo/Redo, persistence     |
| [Workspace and Persistence](./docs/domain/workspace/CONTEXT.md) | one Workspace command or durable save | private edit continuity, attachment fencing, operations, durable revision pointer | electrical semantics, symbol geometry                 |
| [Diagram Presentation](./docs/domain/presentation/CONTEXT.md)   | one reproducible Schematic Projection | symbol rules, Geometry Plans, static spatial projection                           | connectivity, selection, live values, Razor lifecycle |

Compiler and Project Format are translation Modules, not additional bounded contexts: Compiler derives purpose-specific artifacts from a Project Revision, while Project Format decodes an untrusted `.logiclab` carrier into an Import Candidate. Neither changes Circuit Authoring facts.

## Relationships

| Producer           | Consumer             | Translation                                                                                           | Rejected coupling                                    |
| ------------------ | -------------------- | ----------------------------------------------------------------------------------------------------- | ---------------------------------------------------- |
| Circuit Authoring  | Compiler             | Project Revision to Compilation Artifact and Source Map                                             | runtime ordinals as authored identity                |
| Compiler           | Simulation           | opaque executable artifact with complete provenance                                                   | Simulation mutating the Project Document             |
| Circuit Authoring  | Diagram Presentation | Component Contracts and presentation choices to Geometry Plans                                        | coordinates deciding Net membership                  |
| Workspace          | Web                  | semantic Workspace Projection and stable changed-entity identities                                    | Application producing Canvas or SVG patches          |
| Simulation         | Web                  | probe values, Trace transitions, and diagnostics keyed by source identity                             | runtime indexes crossing the browser seam            |
| Project Format     | Workspace            | validated Import Candidate                                                                            | ZIP or JSON DTOs entering Domain as trusted values   |
| Durable repository | Workspace            | immutable Project Revision snapshots under optimistic concurrency                                     | long-lived EF tracking graph or last-write-wins save |

## Future context

[Boolean Analysis](./docs/domain/boolean-analysis/CONTEXT.md) is a deferred
language draft, not a V1 bounded context or dependency. Activating it requires
the explicit owner updates listed in the
[future capability plan](./docs/implementation-plan.md#future-capability-plan).
