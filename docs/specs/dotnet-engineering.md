# .NET Engineering

> **Status:** normative repository contract
>
> **Target:** .NET 10, C# 14, ASP.NET Core 10
>
> **Live versions:** `global.json`, `Directory.Packages.props`, and application-root lock files

This document owns build, dependency, language, runtime, configuration,
observability, test, and publication rules. [Architecture](../../ARCHITECTURE.md)
owns module boundaries, focused specifications own behavior, and the
[Policy Catalog](../policies/catalog.md) owns limits.

## 1. Executable repository policy

| File | Authority |
| --- | --- |
| `global.json` | exact SDK, roll-forward policy, prerelease policy, and Microsoft Testing Platform selection |
| `Directory.Build.props` | target framework, language, analysis, checked arithmetic, warning policy, repository/package metadata |
| `tests/Directory.Build.props` | executable test-project shape and lock-file opt-in |
| `Directory.Packages.props` | exact direct package versions and Central Package Management policy |
| `NuGet.Config` | cleared package and vulnerability-audit sources |
| `.editorconfig` | enforceable text and C# style |
| `.gitattributes` / `.gitignore` | line-ending normalization and repository-specific generated/local exclusions |
| `logic-lab.slnx` | complete executable project graph |
| `LICENSE*` | repository and package licensing terms |

The SDK is pinned to a full version with roll-forward disabled. An SDK change is
one reviewed maintenance change and regenerates every affected application-root
lock file. `allowPrerelease` remains explicit because the CLI and Visual Studio
otherwise choose different defaults. See the [`global.json` contract](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#globaljson-schema).

Projects inherit one target framework, an explicit language version, nullable
analysis, implicit usings, checked arithmetic, deterministic compilation, .NET
analyzers, code-style analysis, and warnings as errors. A project override needs
a nearby reason and owning evidence. No project uses `LangVersion=latest`, disables
nullable analysis, suppresses all warnings, or weakens Release-only analysis.
Unsafe code is allowed only under the evidence gate in [.NET Memory and Unsafe
Code](../research/dotnet-memory-and-unsafe.md).

`AnalysisLevel=10-recommended` fixes the analyzer mode across SDK upgrades.
`NU1901` stays visible but is the only global warning-as-error exception;
`NU1902` and above fail. Suppress the narrowest rule at the narrowest scope and
record why. Microsoft documents these controls in [.NET SDK code-analysis
properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#code-analysis-properties).

Production projects never reference tests or benchmarks. Tests reference the
narrowest production owner, and benchmark projects measure only a named production
operation. Add a project only when the same change gives it executable behavior.

## 2. Restore, dependencies, and packages

Every direct package version appears once in `Directory.Packages.props`; project
files use unversioned `PackageReference` items. Floating versions, project-local
overrides, and central transitive pinning are disabled. Constrain a necessary
transitive dependency by making it an intentional direct dependency, not by hiding
the graph behind a global override. This follows [NuGet Central Package
Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management).

`NuGet.Config` clears machine-provided sources. A second package source requires
complete [Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-source-mapping-rules)
before it is enabled. NuGet Audit evaluates all dependencies from low severity;
an advisory suppression names the URL, applicability decision, owner, and expiry.
Audit is never disabled globally. See [Auditing package dependencies for security
vulnerabilities](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).

Executable roots—Web, tests, browser tests, and benchmarks—commit
`packages.lock.json`; common libraries do not claim that their own lock file
controls a consumer's closure. CI restores the solution in locked mode, then
builds and tests without restore. NuGet documents this application/library
distinction under [dependency locking](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies).

Prefer the shared framework and BCL. A package must remove material complexity,
fit the target/runtime and license, and justify its transitive graph. Analyzer and
build-only packages use `PrivateAssets="all"`. Restore is explicit; build and test
never download tools or mutable Web assets.

All projects are non-packable until a real package consumer exists. Shared metadata
provides author, copyright, project/repository URLs, and the SPDX expression
`MIT OR Apache-2.0`. A project that becomes packable must also supply its own useful
description, README, icon when appropriate, tags, release notes, versioning policy,
and package-specific compatibility evidence. It uses `PackageLicenseExpression`,
never deprecated `LicenseUrl` or `IconUrl`. This follows NuGet's [package authoring
guidance](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)
and [composite-license syntax](https://learn.microsoft.com/en-us/nuget/reference/nuspec#license).
Any package that redistributes third-party material also preserves that material's
license and required notices; the project-level expression does not relicense it.

`Microsoft.EntityFrameworkCore.Design` remains a private dependency of the Web
startup project while migrations and contexts live in Infrastructure. EF tooling
discovers the model through the startup project; the design package is neither a
runtime dependency nor a transitive consumer dependency.

## 3. C# surface and ownership

- Types are `internal` unless another production project needs the documented seam.
- Depend on a concrete deep module when behavior does not vary. Add an interface only
  at a real adapter seam; tests alone do not create one.
- Commands, immutable values, and closed outcomes use records or sealed hierarchies
  only when value or variant semantics are real.
- Records do not make referenced arrays immutable. Copy or take exclusive ownership,
  expose no writable alias, and keep interface collections present and read-only.
- Keep spans, pooled owners, mutable builders, EF entities, and browser/JSON records
  behind their owning seam.
- Validate untrusted data once at ingress, then rely on typed invariants internally.
- Expected domain, policy, cancellation, concurrency, and eligibility conditions use
  closed outcomes. Exceptions represent defects or infrastructure failure and are
  translated once.
- Stable reason codes are declared once by their owner. Adapters do not copy literals.
- Do not add a generic result/envelope, service locator, repository-per-entity layer,
  marker interface, or shared `Common` model.

Checked arithmetic is the default. Hashing, modular arithmetic, bit packing, and
protocol truncation use the smallest explicit `unchecked` expression and boundary
tests.

## 4. Async, cancellation, concurrency, and time

I/O uses `Task`/`Task<T>` and the `Async` suffix. CPU modules stay synchronous and
enter Application-owned typed lanes. Razor, Domain, and core modules do not call
`Task.Run`, block on tasks, expose `async void`, or create fire-and-forget work.
`ValueTask` requires measurement and a documented single-consumption contract.

Every asynchronous I/O seam and long-running CPU entry accepts a nonoptional final
`CancellationToken` and forwards it. Cancellation is cooperative and atomic: an
observed request produces the specified cancelled outcome before publication; a
request after commit does not undo the result. An `OperationCanceledException`
counts as cancellation only when its token is the operation token and cancellation
was requested. Linked and timeout token sources are disposed after their consumers
finish.

Queues are typed and bounded, with explicit ordering, capacity, fairness, full
behavior, and shutdown. Locks guard small synchronous invariants and are never held
across `await` or a module call. A mutable Simulation Session is single-consumer.

Simulation Logical Time never uses wall time. Expiry, retention, retries, rate
windows, and testable delays use `TimeProvider`; capture the current time once per
decision and use elapsed-time APIs for durations. Resource ownership is explicit;
use `using`/`await using`, and transfer stream ownership only when the interface says
so.

## 5. Dependency injection and configuration

`LogicLab.Web` is the composition root and uses the built-in container. Registrations
are explicit and grouped by owner. Constructors are synchronous. Configuration never
calls `BuildServiceProvider`, resolves from a static provider, or uses service
location. Development, test, and CI hosts validate scopes and service construction.
Microsoft's [DI guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#recommendations)
owns the platform constraints.

Singletons are immutable or thread-safe and retain bounded state; circuits own only
presentation coordination; hosted work creates explicit operation scopes; contexts,
streams, parsers, and scratch are short-lived. Long-lived Workspaces never capture a
request or EF scope.

Feature-owned options bind through `OptionsBuilder`, validate shape and cross-field
rules with `ValidateOnStart`, and fail readiness when invalid. Secrets come from the
deployment provider and are never committed or logged. Reloadable configuration
builds one complete immutable versioned snapshot before an atomic swap; admitted work
keeps the snapshot required by its contract.

## 6. Data and transport boundaries

Each external JSON seam owns its `System.Text.Json` options, cloned from .NET 10
[`JsonSerializerOptions.Strict`](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#strict-json-serialization-options)
and paired with one source-generated context plus explicit closed converters. Strict
serialization rejects unknown/duplicate members and honors nullable/required metadata,
but it does not replace lexical, size, policy, canonical-byte, or Domain validation.
JSON, browser, Domain, and EF values remain distinct types.

V1 has no public JSON API and no speculative OpenAPI/controller layer. HTTP routes
are Minimal API or Razor adapters over typed Application calls. Non-success responses
use the [HTTP Boundary](../contracts/http-boundary.md) through one registered Problem
Details adapter. Browser messages are build-bound closed records exchanged in batches;
pointer samples and core object graphs never cross interop.

## 7. Observability and sensitive data

Core deterministic modules return Diagnostics and work evidence; they do not log or
depend on exporters. Application, Infrastructure, and Web own stable `ActivitySource`,
`Meter`, and source-generated `LoggerMessage` instrumentation at admission, queue,
module, repository, transfer, and publication boundaries.

Metrics use stable names, units, descriptions, and low-cardinality dimensions. Never
record project content, names, source locations, tokens, URLs, Session IDs, Trace
values, exception text, or unbounded identities as log payloads or metric tags.
Correctness never depends on a listener or exporter.

## 8. Performance and publication

Correct scalar code and full recomputation are the oracle. LINQ is acceptable off hot
paths; loops, frozen collections, pooling, SIMD, parallelism, caching, and unsafe code
are local measured replacements. BenchmarkDotNet, browser traces, load tests, counters,
and profiles answer different questions. No local mean becomes a product promise.

The baseline Web artifact is framework-dependent, untrimmed, and JIT-compiled.
Trimming, single-file, ReadyToRun, self-contained, GC/ThreadPool tuning, and runtime
switches require a named deployment profile and warning-clean end-to-end evidence.
Native AOT is not a V1 Web profile because [ASP.NET Core lists Blazor Server as
unsupported](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0#limitations-of-the-native-aot-deployment-model).
Invariant globalization is forbidden for the supported cultures.

Release artifacts retain the symbols needed by the diagnostics process and include a
deterministic source/asset build fingerprint, SBOM/provenance, and exact dependency
evidence.

## 9. Verification and test evidence

```sh
dotnet restore logic-lab.slnx --locked-mode --nologo
dotnet build logic-lab.slnx --no-restore --nologo
pwsh tests/LogicLab.Web.BrowserTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test --solution logic-lab.slnx --no-build --no-restore
dotnet format logic-lab.slnx --verify-no-changes --no-restore
git diff --check
```

Browser installation is explicit environment provisioning, never a test hook. In
.NET 10 MTP mode, test options are forwarded directly; use `--treenode-filter` for
selection and do not pass `--nologo` to `dotnet test`. A filtered job must fail on an
empty match.

TUnit source-generated discovery and awaited assertions are the default. Use
`TUnit.FsCheck` for genuine semantic properties with domain generators, shrinking,
replay, and an independent oracle; keep named boundary examples. Tests isolate files,
databases, hosts, ports, browser contexts, cultures, and process state before using a
keyed concurrency constraint. Ordering, sleeps, repeats, and retries never repair a
deterministic test.

bUnit proves Razor projection; TUnit.AspNetCore proves host seams;
TUnit.Playwright proves browser behavior; BenchmarkDotNet stays outside `dotnet test`.
Tests assert caller-visible contracts, not localized prose, private structure, or
predicted performance. Coverage and test counts are telemetry, not release gates.
Detailed framework rationale and lifecycle guidance remain in [.NET Testing Platform
Evidence](../research/testing-platform.md).

These gates prove repository health, not production qualification. Policies, provider
configuration, migrations, backup/restore, browser/security/load evidence, telemetry,
alerts, and runbooks remain [production-qualification work](../implementation-plan.md#production-qualification).
