# Logic Lab

[![CI](https://github.com/romeoahmed/logic-lab/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/romeoahmed/logic-lab/actions/workflows/ci.yml)
[![CodeQL](https://github.com/romeoahmed/logic-lab/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/romeoahmed/logic-lab/actions/workflows/codeql.yml)

Logic Lab is a digital-logic workbench for teaching and experimentation. Build
circuits on a Canvas schematic, simulate deterministic four-state `0/1/X/Z` behavior,
and inspect values and waveforms in the integrated logic analyzer.

> **Project status:** V1 core modules, Workbench conformance, component evidence,
> and the local qualification corpus are implemented and verified. Production
> calibration and environment qualification remain open; [Delivery](./docs/delivery.md)
> records the evidence and remaining work. No Azure environment is represented as
> production-qualified.

## What you can do

- Place components, route wires, and navigate hierarchical circuits.
- Simulate combinational, sequential, clocked, register, counter, ROM, and RAM circuits.
- Apply inputs, Step or Run a Session, and observe Nets through Probes and waveforms.
- Save projects to your account and exchange `.logiclab` packages.
- Start with an inverter, a multiplexer, or a carry-lookahead or bit-serial 4-bit adder.
- Use the workbench in English or Simplified Chinese.

Boolean explanation, Truth Tables, Karnaugh Maps, and automated simplification are
deliberately outside V1. Their design is retained as a non-normative
[future proposal](./docs/future/boolean-analysis.md).

## Get started

- the .NET SDK selected by [`global.json`](./global.json);
- a browser supported by ASP.NET Core Blazor; and
- PostgreSQL 18 when using accounts, durable projects, or database integration tests.

The anonymous Sandbox works without PostgreSQL. From the repository root, run:

```sh
dotnet run --project src/LogicLab.Web/LogicLab.Web.csproj --launch-profile https
```

Open `https://localhost:7148` and choose a starter circuit, or begin from an empty
Sandbox. The same launch profile also serves `http://localhost:5151`.

For accounts and saved projects, create a local PostgreSQL database, then run both
migration sets before starting Web with the same connection:

```sh
export ConnectionStrings__LogicLab='Host=localhost;Database=logiclab;Username=postgres;Password=postgres'
dotnet run --project src/LogicLab.DatabaseMigrator -- local
dotnet run --project src/LogicLab.Web --launch-profile https
```

`local` accepts only a loopback host. Production migration and principal bootstrap
use managed identity through the release runbook.

Try the inverter:

1. Open **Inverter basics** on the Workbench start page.
2. Choose **Start simulation**. The output appears in the logic analyzer.
3. Enter `1` in the Inspector's input field and choose **Apply inputs**. On narrow
   screens, open **Inspect selection** to show the input controls.
4. Choose **Step**. The output becomes `0`; apply `0` and step again to see `1`.

Examples open compiled. After editing a circuit, choose **Compile**. For an existing
Simulation Session, choose **Apply changes** to retain compatible state or
**Restart simulation** to reset it. **Project options** contains import, export,
and the action for saving a Sandbox to your account.

The in-app `/help/getting-started` page covers the editor controls. The
[production runbook](./docs/deployment/runbook.md) covers Azure setup and releases.

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
For the complete Release gate, follow the [component evidence procedure](./conformance/README.md),
which also verifies the qualification corpus and preserves the fresh test reports.

## Repository map

The .NET 10 modular monolith uses Blazor Static SSR for conventional pages and
Interactive Server for the editor. JavaScript adapters handle Canvas painting and
pointer interaction; PostgreSQL stores projects and Identity. [Architecture](./docs/architecture.md)
defines module ownership and dependencies.

| Path                                                                 | Responsibility                                                            |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| [`src/LogicLab.Domain/`](./src/LogicLab.Domain/)                     | authored circuit model, component contracts, and Project Editor           |
| [`src/LogicLab.Engine/`](./src/LogicLab.Engine/)                     | Compiler, Simulation Runtime, and four-state kernels                      |
| [`src/LogicLab.Application/`](./src/LogicLab.Application/)           | Editor Workspace, use cases, and bounded work coordination                |
| [`src/LogicLab.Presentation/`](./src/LogicLab.Presentation/)         | declarative TeachingMixed geometry and schematic projection               |
| [`src/LogicLab.ProjectFormat/`](./src/LogicLab.ProjectFormat/)       | strict `.logiclab` package reader and writer                              |
| [`src/LogicLab.Infrastructure/`](./src/LogicLab.Infrastructure/)     | PostgreSQL persistence and Identity adapters                              |
| [`src/LogicLab.DatabaseMigrator/`](./src/LogicLab.DatabaseMigrator/) | local and production EF Core migrations; production principal bootstrap   |
| [`src/LogicLab.Web/`](./src/LogicLab.Web/)                           | Blazor host, UI, HTTP endpoints, and browser adapters                     |
| [`tests/`](./tests/)                                                 | semantic, application, infrastructure, component, and browser evidence    |
| [`infra/`](./infra/)                                                 | Azure Container Apps, PostgreSQL, storage, identity, and monitoring Bicep |

## Documentation and contributing

The [Documentation Map](./docs/README.md) links product behavior, domain terminology,
specifications, contracts, engineering rules, and research. [Delivery](./docs/delivery.md)
records completion; specifications describe the target behavior.

Start with [Contributing](./CONTRIBUTING.md) for development and pull-request guidance.
Report reproducible bugs and focused proposals in
[GitHub Issues](https://github.com/romeoahmed/logic-lab/issues).

## License

Logic Lab is available under either the [MIT License](./LICENSE-MIT) or the
[Apache License 2.0](./LICENSE-APACHE), at your option (`MIT OR Apache-2.0`).
[Third-party notices](./THIRD-PARTY-NOTICES.md) identify separately licensed bundled
material; [Contributing](./CONTRIBUTING.md#contribution-license) states the contribution terms.
