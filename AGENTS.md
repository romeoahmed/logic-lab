# Repository Guidelines

## Project Structure & Module Organization

Logic Lab is currently an implementation-ready documentation and root-tooling baseline; `logic-lab.slnx` contains no projects yet. Start with `README.md`, then consult `ARCHITECTURE.md` for ownership, `WORKBENCH.md` for product behavior, `CONTEXT-MAP.md` for domain language, and `docs/specs/dotnet-engineering.md` for build/runtime rules. Detailed material lives under `docs/`: `specs/` defines observable behavior, `contracts/` defines application/browser/HTTP seams, `adr/` records decisions, `domain/` holds bounded-context glossaries, `policies/` owns limits, and `research/` preserves evidence. The root PDFs are standards references, not source assets.

When implementation begins, preserve the project seams named in `ARCHITECTURE.md`: `LogicLab.Domain`, `LogicLab.Engine`, `LogicLab.BooleanAnalysis`, `LogicLab.Presentation`, `LogicLab.ProjectFormat`, `LogicLab.Application`, `LogicLab.Infrastructure`, and `LogicLab.Web`. Keep test and benchmark projects separate from production projects.

## Build, Test, and Development Commands

- `dotnet build logic-lab.slnx --nologo` validates the solution. Today it succeeds with a no-projects warning.
- `dotnet test --solution logic-lab.slnx --nologo` is the whole-solution test command once a test project exists. With the current empty MTP-enabled `.slnx`, SDK 10.0.302 exits nonzero with `The solution configuration '|' is invalid`; this is an empty-baseline limitation, not a test failure.
- `dotnet format logic-lab.slnx --verify-no-changes` becomes the formatting gate when the first project exists; today the empty solution has no source to format.
- `git diff --check` catches whitespace errors in documentation changes.

Use the `global.json`-selected .NET 10 SDK feature band and C# 14. Do not introduce package versions outside `Directory.Packages.props`; application-root lock files are added with the projects that consume packages.

## Coding Style & Naming Conventions

Use four-space indentation for C#, LF-normalized text, nullable reference types, implicit usings, checked arithmetic, deterministic builds, analyzers, and warnings as errors. Follow .NET naming: `PascalCase` for types and public members, `camelCase` for locals and parameters, and `I` prefixes only for genuine interfaces at real seams. Model immutable commands and closed outcomes with records or sealed hierarchies, remembering that records do not make referenced arrays immutable. In documentation, use the exact glossary terms and define each fact once in its authoritative document.

## Testing Guidelines

Use xUnit v3 on Microsoft Testing Platform. Add FsCheck for semantic properties, bUnit for Razor projections, Playwright for browser interaction, and BenchmarkDotNet only for comparative benchmarks. Name test classes `{Subject}Tests` and tests `Method_Scenario_Outcome`. Match evidence to ownership; UI tests do not prove simulation semantics. Coverage is supporting telemetry, not the release gate.

## Commit & Pull Request Guidelines

Use small, imperative Conventional Commit messages such as `docs: clarify simulation terminology` or `feat: add scalar gate evaluator`. PRs should explain intent, list affected authoritative documents or modules, link relevant issues/ADRs, and report verification. Include screenshots for visual changes and call out contract, format, security, or dependency changes explicitly.
