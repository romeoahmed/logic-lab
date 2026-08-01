# Logic Lab

Logic Lab is a teaching-oriented gate-level digital logic workbench for authoring, simulating, observing, and simplifying combinational and sequential circuits.

Implementation has begun on the documentation and root-tooling baseline for .NET 10, C# 14, and a server-first Blazor Web App. The first three slices supply the Domain-owned minimum `logiclab.core` schema, the Engine-owned scalar four-state oracle plus private packed Logic Vector operations with differential evidence, and immutable Domain-owned Project lineage with Project Genesis plus narrow place, connect, and move editing; the broader Workbench remains target design.

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

Continue implementation from the vertical-slice sequence in Architecture and the [Implementation Plan](./docs/implementation-plan.md). The documentation baseline closes V1 behavior and interfaces; policy calibration and provider-specific production evidence remain explicit release work in the [Documentation Map](./docs/README.md#development-readiness).

The repository-local [IEEE 91A reference](./00027895.pdf) and [IEEE 1800-2023 reference](./1800-2023.pdf) support standards research. They are references, not project source code.
