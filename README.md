# Logic Lab

Logic Lab is a teaching-oriented gate-level digital logic workbench for authoring, simulating, observing, and simplifying combinational and sequential circuits.

The repository targets .NET 10, C# 14, and a server-first Blazor Web App. Normative documents define the V1 target; the [Implementation Plan](./docs/implementation-plan.md#delivery-status) is the sole delivery ledger and identifies the current dependency frontier.

## Run locally

```shell
dotnet run --project src/LogicLab.Web/LogicLab.Web.csproj --launch-profile https
```

Open `https://localhost:7148`. The launch profile also exposes HTTP on port `5151`.

## Project guides

1. [Documentation Map](./docs/README.md) — reading paths, authority, and evidence
2. [Architecture](./ARCHITECTURE.md) — scope, ownership, and dependency direction
3. [Workbench](./WORKBENCH.md) — target product experience
4. [Context Map](./CONTEXT-MAP.md) — domain language and translations
5. [.NET Engineering Baseline](./docs/specs/dotnet-engineering.md) — repository-wide implementation rules

Delivery status lives only in the [Implementation Plan](./docs/implementation-plan.md#delivery-status).
