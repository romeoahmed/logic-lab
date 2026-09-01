# Contributing to Logic Lab

Thank you for helping improve Logic Lab. A useful contribution preserves the
project's semantic clarity: authored topology, compiled representation, runtime
state, presentation, browser behavior, and persistence each keep one owner.

## Before you start

1. Read the [Documentation Map](./docs/README.md) and the repository
   [guidelines](./AGENTS.md).
2. Check [Delivery](./docs/delivery.md) for the current frontier and search existing
   issues and pull requests for overlapping work.
3. For a behavior, interface, format, dependency, or deployment change, identify the
   owning specification, contract, policy, ADR, or executable configuration before
   editing.
4. Read the target, its tests, one analogous implementation, and the directly
   governing interface or rule.

Focused bug fixes and documentation corrections can go straight to a pull request.
For a large or hard-to-reverse change, open a focused proposal first so scope and
ownership can be agreed before implementation grows.

## Development setup

Use the SDK selected by [`global.json`](./global.json). The repository uses centrally
managed package versions and committed lock files; do not hand-edit lock files or add
versions directly to project files.

Restore and build from the repository root:

```sh
dotnet restore logic-lab.slnx --locked-mode --nologo
dotnet build logic-lab.slnx --no-restore --nologo
```

Run the anonymous Sandbox editor with:

```sh
dotnet run --project src/LogicLab.Web/LogicLab.Web.csproj --launch-profile https
```

PostgreSQL 18 is required for durable-project, Identity, and database integration
work. The full test suite reads an administrative connection from
`LOGICLAB_TEST_POSTGRES_CONNECTION_STRING` and creates isolated temporary databases;
never point it at a shared or production database.

## Make a change

- Branch from an up-to-date `main` and keep each commit focused on one coherent
  change.
- Follow the module boundaries in [Architecture](./docs/architecture.md) and the
  repository rules in [Engineering](./docs/engineering.md).
- Prefer a deep concrete module. Add an interface only at a real adapter seam or when
  production behavior genuinely varies.
- Keep expected failures in closed outcomes, publish atomically, and preserve stable
  source identity and deterministic ordering.
- Put direct package versions in `Directory.Packages.props`; regenerate application
  lock files with the package manager.
- Update the owning document rather than copying the same fact into several files.
  Update indexes and relative links whenever a document moves.
- Use small imperative Conventional Commit subjects, for example
  `fix: preserve probe order on hot swap`.

### Tests

Match evidence to the behavior's owner. Add or change a test when observable behavior
or regression evidence changes; do not pin private implementation structure merely
to increase test count.

- TUnit and Microsoft Testing Platform own executable tests.
- `TUnit.FsCheck` owns generative semantic properties.
- bUnit owns Razor projections; Playwright owns browser interaction.
- BenchmarkDotNet owns comparative microbenchmarks, not acceptance behavior.
- UI tests do not prove Simulation semantics, and retries do not conceal deterministic
  failures.

See [Engineering](./docs/engineering.md) for the complete test and dependency policy.

## Verify

Run the gates relevant to the change. Before requesting review, the normal full
checkout is:

```sh
dotnet build logic-lab.slnx --nologo
dotnet test --solution logic-lab.slnx
dotnet format logic-lab.slnx --verify-no-changes
git diff --check
```

Documentation-only changes do not need unrelated runtime tests, but their local links,
anchors, terminology, and ownership still need review. Infrastructure changes also
require Bicep format and compile checks before any Azure-backed validation.

## Open a pull request

Keep the pull request narrow enough to review as one idea. Its description should:

- explain the intent and user-visible or architectural effect;
- link the affected authoritative documents, ADRs, and issues;
- list the exact verification performed and any intentionally omitted gate;
- call out interface, format, security, dependency, lock-file, or deployment changes;
  and
- include screenshots for visible UI changes.

Before submitting, review the complete diff for generated artifacts, unrelated edits,
secrets, stale names, broken links, and accidental compatibility layers. A clean pull
request leaves the repository understandable without relying on discussion history.
