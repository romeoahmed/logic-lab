# Domain Map

Domain documents own project language, not implementation structure. Architecture
owns modules and dependencies; specifications own observable rules.

## Contexts

| Context                   | Owns                                                                                  | Glossary                        |
| ------------------------- | ------------------------------------------------------------------------------------- | ------------------------------- |
| Circuit Authoring         | Project lineage, authored topology, revisions, and edit intent                        | [terms](./circuit-authoring.md) |
| Simulation                | four-state values, compilation provenance, session state, logical time, and Trace     | [terms](./simulation.md)        |
| Workspace and Persistence | editor continuity, durable location, save, import/export handoff, and background work | [terms](./workspace.md)         |
| Diagram Presentation      | renderer-neutral symbols, geometry, and schematic projection                          | [terms](./presentation.md)      |

Compiler and Project Format are translation modules, not additional bounded
contexts. Web translates domain projections into browser state; browser selection,
focus, viewport, and pointer preview are presentation state rather than domain facts.

## Relationships

```text
Circuit Authoring --Project Revision--> Compiler --Compilation Artifact--> Simulation
        |                    |                                        |
        |                    +--> Diagram Presentation                +--> Trace
        |                                                               |
        +<---------------- Editor Workspace ----------------------------+
                               |
                 Project Format / durable repository
                               |
                              Web
```

- Project Format decodes an untrusted carrier into an Import Candidate; Project
  Editor creates Project Genesis.
- Compiler derives executable identity and Source Map without changing authored
  identity.
- Simulation owns committed runtime state; it never writes the Project Document.
- Editor Workspace owns history, attachment fencing, save, compilation, and Session
  coordination.
- Diagram Presentation derives static geometry; Web composes transient and live
  overlays.

Boolean explanation and simplification remain a [future proposal](../future/boolean-analysis.md),
not a V1 bounded context.
