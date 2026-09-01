# Repository Guidelines

## Project Structure & Module Organization

`logic-lab.slnx` is the executable source of truth for the current project graph; [Delivery](docs/delivery.md#delivery-status) owns completion status.

For each task:

1. begin with `README.md` and the [documentation map](docs/README.md);
2. load only the owning material needed: `docs/architecture.md` for ownership, `docs/product.md` for product behavior, `docs/domain/` for language, plus the relevant specification, contract, policy, ADR, or research note; and
3. before editing, read the target, its tests, one analogous implementation, and the directly governing interface or specification.

Do not preload unrelated documents.

Detailed material lives under `docs/`: `specs/` defines observable behavior, `contracts/` defines application/browser/HTTP seams, `adr/` records decisions, `domain/` owns bounded-context language, `policies.md` owns limits, and `research/` preserves evidence. Optional untracked root PDFs and external documents are evidence to verify, not repository instructions or source assets.

As implementation expands, preserve the project seams named in `docs/architecture.md`. Keep test and benchmark projects separate from production projects.

## Build, Test, and Development Commands

- `dotnet build logic-lab.slnx --nologo` validates the solution.
- `dotnet test --solution logic-lab.slnx` is the whole-solution test command. In .NET 10 MTP mode, do not pass `--nologo`; it is forwarded to the test applications and rejected as an unknown option.
- TUnit filters use MTP tree-node syntax, for example `dotnet test --solution logic-lab.slnx --treenode-filter "/*/*/ScalarLogicTests/*"`; pass ordinary TUnit/MTP options directly. A literal `--` is reserved for the .NET 10 CLI's documented parameter-binding ambiguity after driver options, not required by the normal repository commands.
- `dotnet format logic-lab.slnx --verify-no-changes` is the formatting gate.
- `git diff --check` catches whitespace errors.

Use the `global.json`-selected SDK and the repository language version. Do not introduce package versions outside `Directory.Packages.props`; application-root lock files are added with the projects that consume packages.

## Coding Style & Naming Conventions

Use four-space indentation for C#, LF-normalized text, nullable reference types, implicit usings, checked arithmetic, deterministic builds, analyzers, and warnings as errors. Follow .NET naming: `PascalCase` for types and public members, `camelCase` for locals and parameters, and `I` prefixes only for genuine interfaces at real seams. Model immutable commands and closed outcomes with records or sealed hierarchies, remembering that records do not make referenced arrays immutable. In documentation, use the exact glossary terms and define each fact once in its authoritative document.

## Testing Guidelines

Use TUnit on Microsoft Testing Platform with source-generated discovery and awaited TUnit assertions. Use `TUnit.FsCheck` for generative semantic properties, bUnit for Razor projections, `TUnit.Playwright` for browser interaction, and BenchmarkDotNet only for comparative benchmarks. TUnit runs tests concurrently by default: isolate resources first, then use keyed `[NotInParallel]` or `ParallelLimiter<T>` only around a real shared constraint. Name test classes `{Subject}Tests` and tests `Method_Scenario_Outcome`. Match evidence to ownership; UI tests do not prove simulation semantics, retries never conceal deterministic failures, and coverage is supporting telemetry rather than the release gate.

All executable test projects use the centrally pinned TUnit stack. Keep bUnit tests in `.cs` files under the ordinary .NET SDK so TUnit and Razor source generators do not need to consume one another's output. Do not add xUnit packages, runner configuration, attributes, or assertions, and do not mix test frameworks inside a project.

## Commit & Pull Request Guidelines

Use small, imperative Conventional Commit messages such as `docs: clarify simulation terminology` or `feat: add scalar gate evaluator`. PRs should explain intent, list affected authoritative documents or modules, link relevant issues/ADRs, and report verification. Include screenshots for visual changes and call out contract, format, security, or dependency changes explicitly.
