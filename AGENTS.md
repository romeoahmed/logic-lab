# Repository Guidelines

## Project Structure & Module Organization

Logic Lab has completed implementation-plan slices `01` through `07`: `logic-lab.slnx` contains `LogicLab.Domain`, `LogicLab.Engine`, `LogicLab.Application`, `LogicLab.Presentation`, `LogicLab.Web`, and their test projects. Start with `README.md`, then consult `ARCHITECTURE.md` for ownership, `WORKBENCH.md` for product behavior, `CONTEXT-MAP.md` for domain language, and `docs/specs/dotnet-engineering.md` for build/runtime rules. Detailed material lives under `docs/`: `specs/` defines observable behavior, `contracts/` defines application/browser/HTTP seams, `adr/` records decisions, `domain/` holds bounded-context glossaries, `policies/` owns limits, and `research/` preserves evidence. The root PDFs are standards references, not source assets.

As implementation expands, preserve the project seams named in `ARCHITECTURE.md`: `LogicLab.Domain`, `LogicLab.Engine`, `LogicLab.BooleanAnalysis`, `LogicLab.Presentation`, `LogicLab.ProjectFormat`, `LogicLab.Application`, `LogicLab.Infrastructure`, and `LogicLab.Web`. Keep test and benchmark projects separate from production projects.

## Build, Test, and Development Commands

- `dotnet build logic-lab.slnx --nologo` validates the solution.
- `dotnet test --solution logic-lab.slnx` is the whole-solution test command. In .NET 10 MTP mode, do not pass `--nologo`; it is forwarded to the test applications and rejected as an unknown option.
- TUnit filters use MTP tree-node syntax, for example `dotnet test --solution logic-lab.slnx --treenode-filter "/*/*/ScalarLogicTests/*"`; .NET 10 passes platform arguments directly without an extra `--` separator.
- `dotnet format logic-lab.slnx --verify-no-changes` is the formatting gate.
- `git diff --check` catches whitespace errors.

Use the `global.json`-selected .NET 10 SDK feature band and C# 14. Do not introduce package versions outside `Directory.Packages.props`; application-root lock files are added with the projects that consume packages.

## Coding Style & Naming Conventions

Use four-space indentation for C#, LF-normalized text, nullable reference types, implicit usings, checked arithmetic, deterministic builds, analyzers, and warnings as errors. Follow .NET naming: `PascalCase` for types and public members, `camelCase` for locals and parameters, and `I` prefixes only for genuine interfaces at real seams. Model immutable commands and closed outcomes with records or sealed hierarchies, remembering that records do not make referenced arrays immutable. In documentation, use the exact glossary terms and define each fact once in its authoritative document.

## Testing Guidelines

Use TUnit on Microsoft Testing Platform with source-generated discovery and awaited TUnit assertions. Use `TUnit.FsCheck` for generative semantic properties, bUnit for Razor projections, `TUnit.Playwright` for browser interaction, and BenchmarkDotNet only for comparative benchmarks. TUnit runs tests concurrently by default: isolate resources first, then use keyed `[NotInParallel]` or `ParallelLimiter<T>` only around a real shared constraint. Name test classes `{Subject}Tests` and tests `Method_Scenario_Outcome`. Match evidence to ownership; UI tests do not prove simulation semantics, retries never conceal deterministic failures, and coverage is supporting telemetry rather than the release gate.

All executable test projects use the centrally pinned TUnit stack. Keep bUnit tests in `.cs` files under the ordinary .NET SDK so TUnit and Razor source generators do not need to consume one another's output. Do not add xUnit packages, runner configuration, attributes, or assertions, and do not mix test frameworks inside a project.

## Commit & Pull Request Guidelines

Use small, imperative Conventional Commit messages such as `docs: clarify simulation terminology` or `feat: add scalar gate evaluator`. PRs should explain intent, list affected authoritative documents or modules, link relevant issues/ADRs, and report verification. Include screenshots for visual changes and call out contract, format, security, or dependency changes explicitly.
