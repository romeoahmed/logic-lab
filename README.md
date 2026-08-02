# Logic Lab

Logic Lab is a teaching-oriented gate-level digital logic workbench for authoring, simulating, observing, and simplifying combinational and sequential circuits.

Phase A is complete on the .NET 10, C# 14, server-first Blazor Web App baseline. Its six executable slices supply the Domain-owned minimum `logiclab.core` schema, the Engine-owned scalar four-state oracle plus private packed Logic Vector operations with differential evidence, immutable Domain-owned Project lineage, deterministic flat combinational Compilation, the first atomic Simulation Session, and an Interactive Server `/editor` tracer through Application-owned Workspace commands and an accessible Presentation projection. Slice `07` now extends that tracer with explicit Net, Junction, and Wire Geometry authoring: connect, merge, split, route, unroute, and Junction edits remain atomic, crossings do not create connectivity, and cancelled edits publish no Project Revision. The broader V1 Workbench remains target design.

## Core capabilities

- hierarchical schematic editor with explicit Net and Junction topology;
- deterministic four-state, zero-delay, event-driven simulation;
- latches, flip-flops, registers, counters, shift registers, ROM, and RAM;
- Logic Analyzer, Truth Table, Karnaugh Map, and proof-gated simplification;
- Transaction History and immutable Project Revisions;
- declarative IEEE 91A-based TeachingMixed symbols;
- strict `.logiclab` import/export and durable authenticated projects.

## Read first

1. [Architecture](./ARCHITECTURE.md)
2. [Workbench](./WORKBENCH.md)
3. [Context Map](./CONTEXT-MAP.md)
4. [Documentation Map](./docs/README.md)
5. [.NET Engineering Baseline](./docs/specs/dotnet-engineering.md)

Continue implementation from the current frontier in the [Implementation Plan](./docs/implementation-plan.md#delivery-status): hierarchy and topology breadth (`08` and `09`), combinational contract breadth (`10` and `11`), and recoverable Workspace control (`17`). The documentation baseline closes V1 behavior and interfaces; policy calibration and provider-specific production evidence remain explicit release work in the [Documentation Map](./docs/README.md#development-readiness).

The repository-local [IEEE 91A reference](./00027895.pdf) and [IEEE 1800-2023 reference](./1800-2023.pdf) support standards research. They are references, not project source code.
