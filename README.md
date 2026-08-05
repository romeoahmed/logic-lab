# Logic Lab

Logic Lab is a teaching-oriented gate-level digital logic workbench for authoring, simulating, observing, and simplifying combinational and sequential circuits.

The repository targets .NET 10, C# 14, and a server-first Blazor Web App. Normative documents describe the intended V1 product, not the current delivery state. Use the [Implementation Plan](./docs/implementation-plan.md#delivery-status) for completed slices and [Development Readiness](./docs/README.md#development-readiness) for the executable evidence and open gaps.

## Documentation boundary

The [Architecture](./ARCHITECTURE.md#1-baseline-and-product-scope) owns V1 scope and exclusions. The [Workbench](./WORKBENCH.md) owns the target experience, while specifications and seam contracts own observable behavior and exchanged values. Do not infer implementation from any normative document; incomplete plan items remain target design.

## Read first

1. [Documentation Map](./docs/README.md)
2. [Architecture](./ARCHITECTURE.md)
3. [Workbench](./WORKBENCH.md)
4. [Context Map](./CONTEXT-MAP.md)
5. [.NET Engineering Baseline](./docs/specs/dotnet-engineering.md)

Continue from the current frontier in the [Implementation Plan](./docs/implementation-plan.md#dependency-frontier). The documentation baseline closes V1 behavior and interfaces; policy calibration and provider-specific production evidence remain explicit release work.
