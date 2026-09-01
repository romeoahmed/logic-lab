# Logic Lab

[![CI](https://github.com/romeoahmed/logic-lab/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/romeoahmed/logic-lab/actions/workflows/ci.yml)
[![CodeQL](https://github.com/romeoahmed/logic-lab/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/romeoahmed/logic-lab/actions/workflows/codeql.yml)

Logic Lab is a teaching-oriented digital-logic workbench for building, simulating,
and observing gate-level circuits. It connects a Canvas-first schematic editor to
deterministic four-state `0/1/X/Z` semantics, discrete Logical Time, and an integrated
logic analyzer.

> **Project status:** V1 behavior is implementation-complete. The component evidence
> manifest and production qualification remain open; see [Delivery](./docs/delivery.md)
> for the exact frontier. Azure deployment assets are implemented, but no environment
> is represented as production-qualified.

## What you can do

- **Author real topology.** Place components, route orthogonal wires, create explicit
  Junctions, build hierarchical Circuit Definitions, and move through revision
  history without deriving identity from pixels.
- **Simulate honest digital behavior.** Explore combinational, sequential, clocked,
  register, counter, ROM, and single-port RAM circuits under deterministic four-state
  semantics, including conservative feedback settlement.
- **Follow cause to observation.** Compile source-linked artifacts, attach Probes,
  inspect live values, step or run a Session, and navigate waveform history with
  explicit Trace Gaps.
- **Keep and exchange work.** Claim durable projects, reopen them under authorization,
  and import or export the strict `.logiclab` package format.
- **Start from useful examples.** Build an inverter, a multiplexer steering circuit,
  or one of two 4-bit adders—carry-lookahead and bit-serial—directly in the editor.
- **Work in English or Simplified Chinese.** The host and editor share localized,
  direction-aware presentation while protocol and diagnostic identities remain
  stable.

Boolean explanation, Truth Tables, Karnaugh Maps, and automated simplification are
deliberately outside V1. Their design is retained as a non-normative
[future proposal](./docs/future/boolean-analysis.md).

## From schematic to trace

```text
Project Revision
      │
      ├── Diagram Presentation ──> Canvas scene
      │
      └── Compiler ──> Compilation Artifact + Source Map
                              │
                              └── Simulation Session ──> values, Probes, Trace
```

Logic Lab is a modular monolith on .NET 10. Conventional pages use Static Server
Rendering; the editor uses per-page Interactive Server rendering. Razor owns commands,
status, and recovery, while collocated JavaScript adapters own frame-rate Canvas
painting and pointer interaction. PostgreSQL stores durable projects and Identity;
the application keeps authored topology, compiled representation, runtime state,
presentation geometry, browser state, and persistence location as separate facts.

Read [Architecture](./docs/architecture.md) for the complete module and dependency
model.

## Get started

### Requirements

- the .NET SDK selected by [`global.json`](./global.json);
- a browser supported by ASP.NET Core Blazor; and
- PostgreSQL 18 when using accounts, durable projects, or database integration tests.

The anonymous Sandbox editor does not require a durable project. From the repository
root, run:

```sh
dotnet run --project src/LogicLab.Web/LogicLab.Web.csproj --launch-profile https
```

Open `https://localhost:7148` and choose a starter circuit, or begin from an empty
Sandbox. The same launch profile also serves `http://localhost:5151`.

A first useful pass through the workbench is:

1. choose a starter or author a circuit;
2. compile the current Project Revision;
3. add a Probe and create a Simulation Session; and
4. apply inputs, Step or Run, then inspect values and waveform history.

The in-app `/help/getting-started` page covers the editor controls. The production
[runbook](./docs/deployment/runbook.md) owns Azure setup and release procedures; it is
not a local-development shortcut.

## Verify a checkout

```sh
dotnet build logic-lab.slnx --nologo
dotnet test --solution logic-lab.slnx
dotnet format logic-lab.slnx --verify-no-changes
git diff --check
```

The full test suite expects an administrative PostgreSQL connection in
`LOGICLAB_TEST_POSTGRES_CONNECTION_STRING`; tests create isolated temporary databases.
CI runs the same repository graph on `ubuntu-26.04-arm` with PostgreSQL 18.

## Repository map

| Path                                                                 | Responsibility                                                            |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| [`src/LogicLab.Domain/`](./src/LogicLab.Domain/)                     | authored circuit model, component contracts, and Project Editor           |
| [`src/LogicLab.Engine/`](./src/LogicLab.Engine/)                     | Compiler, Simulation Runtime, and four-state kernels                      |
| [`src/LogicLab.Application/`](./src/LogicLab.Application/)           | Editor Workspace, use cases, and bounded work coordination                |
| [`src/LogicLab.Presentation/`](./src/LogicLab.Presentation/)         | declarative TeachingMixed geometry and schematic projection               |
| [`src/LogicLab.ProjectFormat/`](./src/LogicLab.ProjectFormat/)       | strict `.logiclab` package reader and writer                              |
| [`src/LogicLab.Infrastructure/`](./src/LogicLab.Infrastructure/)     | PostgreSQL persistence and Identity adapters                              |
| [`src/LogicLab.DatabaseMigrator/`](./src/LogicLab.DatabaseMigrator/) | production principal bootstrap and deterministic EF Core migrations       |
| [`src/LogicLab.Web/`](./src/LogicLab.Web/)                           | Blazor host, UI, HTTP endpoints, and browser adapters                     |
| [`tests/`](./tests/)                                                 | semantic, application, infrastructure, component, and browser evidence    |
| [`infra/`](./infra/)                                                 | Azure Container Apps, PostgreSQL, storage, identity, and monitoring Bicep |

Executable configuration is authoritative for the SDK, package versions, project
graph, workflows, and infrastructure values. Normative documents describe the V1
target; only the Delivery record states what has shipped.

## Documentation

| Need                                                  | Start here                                                    |
| ----------------------------------------------------- | ------------------------------------------------------------- |
| product behavior and interaction                      | [Product](./docs/product.md)                                  |
| system shape and dependencies                         | [Architecture](./docs/architecture.md)                        |
| domain terminology                                    | [Domain Map](./docs/domain/README.md)                         |
| delivery status and next work                         | [Delivery](./docs/delivery.md)                                |
| build, dependency, and test rules                     | [Engineering](./docs/engineering.md)                          |
| focused specifications, contracts, ADRs, and research | [Documentation Map](./docs/README.md)                         |
| selected Azure shape and remaining qualification      | [Production Profile](./docs/deployment/production-profile.md) |

Read the shallowest document that answers the question; deep specifications remain
available when exact ordering, outcome, format, or policy rules matter.

## Contributing and help

Contributions are welcome. Start with [CONTRIBUTING.md](./CONTRIBUTING.md) for scope,
workflow, verification, and pull-request expectations. Use
[GitHub Issues](https://github.com/romeoahmed/logic-lab/issues) for reproducible bugs
and focused proposals; consult the in-app help or [Documentation Map](./docs/README.md)
before opening a usage question.

## License

Logic Lab is available under either the [MIT License](./LICENSE-MIT) or the
[Apache License 2.0](./LICENSE-APACHE), at your option. See [LICENSE](./LICENSE) for
the dual-license terms and [third-party notices](./THIRD-PARTY-NOTICES.md) for
separately licensed bundled material.
