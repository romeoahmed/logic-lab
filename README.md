# Logic Lab

Logic Lab is a teaching-oriented digital-logic workbench for building, simulating,
and observing gate-level circuits. It uses deterministic four-state
`0/1/X/Z` semantics and a server-first Blazor Web App on .NET 10.

## Quick start

Install the SDK pinned by [`global.json`](./global.json), then run:

```sh
dotnet run --project src/LogicLab.Web/LogicLab.Web.csproj --launch-profile https
```

Open `https://localhost:7148`. The same profile serves HTTP on
`http://localhost:5151`.

## Find your way around

| Need                                              | Start here                                                           |
| ------------------------------------------------- | -------------------------------------------------------------------- |
| current delivery status                           | [Implementation Plan](./docs/implementation-plan.md#delivery-status) |
| project and dependency boundaries                 | [Architecture](./ARCHITECTURE.md)                                    |
| product behavior and visual language              | [Workbench](./WORKBENCH.md)                                          |
| domain terminology                                | [Context Map](./CONTEXT-MAP.md)                                      |
| build and coding rules                            | [.NET Engineering](./docs/specs/dotnet-engineering.md)               |
| one focused spec, contract, ADR, or evidence note | [Documentation Map](./docs/README.md)                                |

Executable configuration is authoritative for the SDK, package versions, and
project graph. Normative documents describe the V1 target; only the Implementation
Plan states what has shipped.

## Verify

```sh
dotnet build logic-lab.slnx --nologo
dotnet test --solution logic-lab.slnx
dotnet format logic-lab.slnx --verify-no-changes
git diff --check
```

## License

Logic Lab is available under either the [MIT License](./LICENSE-MIT) or the
[Apache License 2.0](./LICENSE-APACHE), at your option. See [LICENSE](./LICENSE)
for the dual-license terms and [third-party notices](./THIRD-PARTY-NOTICES.md)
for separately licensed bundled material.
