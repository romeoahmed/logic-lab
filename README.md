# Logic Lab

Logic Lab is a teaching-oriented gate-level digital logic workbench for authoring, simulating, observing, and simplifying combinational and sequential circuits.

The repository targets .NET 10, C# 14, and a server-first Blazor Web App. The [Implementation Plan](./docs/implementation-plan.md#delivery-status) tracks completed slices; the broader V1 Workbench remains target design. See [Development Readiness](./docs/README.md#development-readiness) for current evidence and gaps instead of inferring delivery from normative documents.

## V1 scope

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

Continue from the current frontier in the [Implementation Plan](./docs/implementation-plan.md#dependency-frontier). The documentation baseline closes V1 behavior and interfaces; policy calibration and provider-specific production evidence remain explicit release work.
