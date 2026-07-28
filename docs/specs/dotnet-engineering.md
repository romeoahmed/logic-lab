# .NET Engineering Baseline

> Status: normative implementation and build contract
> Target: .NET 10, C# 14, ASP.NET Core 10
> SDK: 10.0.302, exact

This specification owns repository-wide .NET build, dependency, language, execution, configuration, serialization, observability, and publication rules. [Architecture](../../ARCHITECTURE.md) continues to own Module seams and dependency direction; focused specifications own behavior; [Policy Catalog](../policies/catalog.md) owns limits. This file does not create a Runtime, Common, or cross-cutting Module.

## 1. Repository and project baseline

The checked-in root files are executable policy:

| File | Owned decision |
|---|---|
| `global.json` | exact SDK `10.0.302`, no roll-forward/prerelease SDK, Microsoft Testing Platform runner |
| `Directory.Build.props` | `net10.0`, C# 14, nullable, analyzers, checked arithmetic, safe code, deterministic warning-clean builds |
| `Directory.Packages.props` | Central Package Management; no project-local package versions |
| `NuGet.Config` | one cleared package source and one explicit vulnerability-audit source |
| `.editorconfig` | repository text and C# style that `dotnet format` can enforce |
| `logic-lab.slnx` | the complete build/test graph as projects are added |

The SDK version is full and `rollForward=disable`, so local and CI restore/build use the same SDK patch as the reviewed package lock graph. Moving to another patch or feature band is one deliberate change that regenerates and reviews application-root lock files. `allowPrerelease` is explicit because its platform default differs between CLI and Visual Studio. See the [.NET SDK selection contract](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#globaljson-schema).

Production projects use these paths and SDKs:

```text
src/LogicLab.Domain/             Microsoft.NET.Sdk
src/LogicLab.Engine/             Microsoft.NET.Sdk
src/LogicLab.BooleanAnalysis/    Microsoft.NET.Sdk
src/LogicLab.Presentation/       Microsoft.NET.Sdk
src/LogicLab.ProjectFormat/      Microsoft.NET.Sdk
src/LogicLab.Application/        Microsoft.NET.Sdk
src/LogicLab.Infrastructure/     Microsoft.NET.Sdk
src/LogicLab.Web/                Microsoft.NET.Sdk.Web
```

Test projects mirror evidence ownership under `tests/`; browser workflow tests use `tests/LogicLab.Web.BrowserTests/`, and comparative benchmarks use `benchmarks/LogicLab.Benchmarks/`. Create a test or benchmark project with the slice that gives it executable evidence—never add empty placeholder projects. Production projects do not reference tests or benchmarks. Tests reference the narrowest production projects that own the fact being proved.

All projects inherit `TargetFramework`, `LangVersion`, nullable context, implicit usings, checked arithmetic, and safe-code defaults. A project may override a central property only with a comment naming the measured or platform reason and the owning evidence. `AllowUnsafeBlocks` can change only under the gate in [.NET Memory and Unsafe Code Research](../research/dotnet-memory-and-unsafe.md). No project suppresses all warnings, disables nullable analysis, uses `LangVersion=latest`, or weakens a warning only in Release.

`AnalysisLevel=10-recommended` makes the analyzer set stable across SDK major upgrades while enabling the .NET 10 recommended rules. Code-style analysis participates in build, and every compiler and analyzer warning fails the build. NuGet low-severity vulnerability warning `NU1901` remains visible but is the sole global warning-as-error exception; `NU1902` through `NU1905` and every other restore warning still fail. Suppression is the narrowest source annotation or rule-specific `.editorconfig` entry, carries a reason, and has a regression test when it masks a correctness rule. Microsoft documents the analyzer version/mode and build-style properties in [.NET SDK code-analysis properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#code-analysis-properties).

## 2. Restore and dependency supply chain

Package versions occur exactly once as exact versions in `Directory.Packages.props`; project files contain only unversioned `PackageReference` items. Floating versions, version overrides, package references in `Directory.Build.props`, and central transitive pinning are disabled. `CentralPackageVersionOverrideEnabled=false` makes an attempted `VersionOverride` fail restore. If a transitive dependency must be constrained, promote it to an intentional direct dependency with a comment and tests instead of turning on repository-wide transitive pinning. [NuGet Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) owns the underlying mechanics.

`NuGet.Config` clears machine-provided sources and uses NuGet v3. A second package source is a security and provenance change: add Package Source Mapping for every direct and transitive package before enabling it. Once mapping exists, NuGet requires every package to match a source pattern ([Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-source-mapping-rules)).

NuGet Audit remains enabled at `all` dependencies and reports from `low`; .NET 10 otherwise already defaults `NuGetAuditMode` to `all` for `net10.0`. Moderate, high, and critical advisories (`NU1902`–`NU1904`) fail restore. A suppression names one advisory URL, the reason risk is accepted, the owner, and an expiry or upgrade issue; disabling audit globally is forbidden. See [NuGet Audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages#configuring-nuget-audit).

Application roots—Web, test executables, browser tests, and benchmarks—enable `RestorePackagesWithLockFile` when created and commit their `packages.lock.json`. Common production libraries do not claim that their own lock file controls an executable's resolved closure. CI runs `dotnet restore logic-lab.slnx --locked-mode --nologo`, then build and test with `--no-restore`; dependency updates deliberately regenerate and review lock files. This follows NuGet's distinction between application and common-library lock files ([locking dependencies](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)).

Prefer the shared framework and BCL before a package. A package must remove material implementation complexity from a Module without weakening its seam, be compatible with .NET 10 and the publication profile, have acceptable license/provenance, and earn its transitive graph. Analyzer/build-only packages use `PrivateAssets="all"`. Dependency acquisition occurs only in explicit restore or environment-provisioning steps; build and test execution never download tools or mutable web assets implicitly.

## 3. C# surface and ownership

- Types are `internal` unless another production project must call them. A public type belongs to one documented Module interface or transport seam; namespace visibility is not architecture.
- Register and depend on concrete deep Modules when behavior does not vary. Define an `I`-prefixed port only at a real seam with production and local/test adapters; never generate an interface for each class merely for dependency injection.
- Commands, requests, immutable values, and closed outcomes use records or sealed hierarchies where value or variant semantics are real. A record containing an array is not immutable and does not gain structural array equality; constructors copy or take exclusive ownership and expose no writable alias.
- Interface collections are present, non-null, owned, and read-only. `Span<T>`, `Memory<T>`, pooled owners, mutable builders, EF entities, and JavaScript/JSON DTOs do not cross Domain or core Module seams.
- Validate untrusted bytes, browser records, HTTP values, configuration, and persistence results at their ingress seam. Inside a typed Module, enforce constructors and invariants once rather than scattering defensive revalidation across every helper.
- Expected domain, policy, cancellation, concurrency, and eligibility conditions return the specification's closed outcomes. Exceptions represent defects or infrastructure failure and are translated once at the Application/host seam; exception messages and types are never caller logic.
- Do not introduce a generic `Result<T>`, generic command envelope, service locator, repository-per-entity interface, marker interface, or shared `Common` model.

Checked arithmetic is the default compiler context. Intentional modular arithmetic, bit packing, hashing, and protocol truncation use the smallest explicit `unchecked` expression and tests at zero, maximum, overflow, and tail boundaries.

## 4. Async, cancellation, concurrency, and time

I/O methods use `Task`/`Task<T>` and the `Async` suffix. CPU Modules remain synchronous and are admitted by the Application-owned typed lanes. Razor, Domain, and core Modules never call `Task.Run`; the Work Coordinator is the one visible place that assigns admitted CPU work to workers. `ValueTask` is allowed only after measurement proves a frequent synchronous-completion path and the interface documents its single-consumption constraints. There is no `async void` outside framework event signatures, no `.Result`, `.Wait()`, fire-and-forget task, or async DI factory. Microsoft distinguishes naturally asynchronous I/O from explicitly scheduled CPU work in [async scenarios](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios).

Every asynchronous I/O seam and every potentially long synchronous Module entry accepts a nonoptional `CancellationToken` as its final parameter and forwards it. Cancellation is cooperative, not thread termination: implementations check at bounded work intervals and translate an observed request into the specified atomic cancelled outcome. A request after commit does not undo publication. When request abort, user cancellation, supersession, policy timeout, and shutdown can coincide, the owning Application adapter retains the source tokens and applies its declared outcome precedence; it never tries to infer cause from one linked token. Linked or timeout `CancellationTokenSource` values are disposed after all consumers terminate ([managed-thread cancellation](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)).

Queues are typed and bounded. Their capacity, fairness, full behavior, and shutdown drain/abort rule come from Scheduling Policy; no unbounded `Channel`, concurrent collection, timer callback, or semaphore becomes a hidden scheduler. Locks protect a small synchronous invariant, are never held across `await`, and do not wrap Module calls. One mutable Simulation Session is single-consumer through its lane; immutable requests for different Modules may run concurrently.

`Logical Time` remains a Simulation value. Wall-clock expiry, retention, retry, rate windows, and testable delays receive `TimeProvider`; production composition supplies `TimeProvider.System`. Capture `GetUtcNow()` once per decision and use monotonic timestamps/elapsed-time APIs for durations. Use `DateTimeOffset` only for wall-clock facts that must be persisted or displayed, never to drive Simulation semantics. `TimeProvider` is the .NET abstraction for retrieving time and creating timers ([API](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider?view=net-10.0)).

Every resource has one visible owner. Prefer `using`/`await using`; DI disposes only instances it creates. A caller-owned `Stream` is never closed by a Module unless its interface says ownership transfers. Finalizers, `GC.Collect`, process-wide ThreadPool tuning, and abandoned background work are forbidden in ordinary implementation.

## 5. Dependency injection and configuration

`LogicLab.Web` is the composition root and uses the built-in container. Registrations are grouped by owning Module, not by a general reflection scan. Constructors are fast and synchronous. Configuration code never calls `BuildServiceProvider`, resolves from a static `IServiceProvider`, or uses runtime service location. Scope validation and build validation are enabled in Development, test hosts, and CI. Microsoft's DI guidance explicitly rejects async resolution, service location, configuration-time `BuildServiceProvider`, and singleton capture of scoped dependencies ([guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#recommendations)).

Lifetime follows ownership:

| Lifetime | Valid examples | Required property |
|---|---|---|
| singleton/process | immutable registries, stateless thread-safe Modules, Workspace directory, Work Coordinator, instruments | no scoped capture; bounded retained state |
| circuit | Web projection coordinator and browser adapters | no Workspace, operation, or `DbContext` ownership |
| operation scope | repository/authorization/transfer adapters for one operation | created explicitly by hosted work |
| short-lived object | `DbContext`, stream, parser, builder, pooled scratch | disposed before operation publication |

Host configuration binds feature-owned options classes through `OptionsBuilder`, validates shape and cross-field rules with `ValidateOnStart`, and fails readiness for invalid required values. Secrets are referenced through the deployment provider, never committed, returned, or logged. The options pattern and startup validation are documented in [Options validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#options-validation).

Configuration does not mutate an in-flight semantic operation. Policies and registries are immutable versioned snapshots captured at command admission. If `IOptionsMonitor` is later used, a change callback builds and validates a complete replacement snapshot before one atomic registry swap; existing Workspaces and operations retain their captured versions where their contracts require it. `IOptionsSnapshot` is not used as a circuit-lifetime cache.

## 6. JSON, HTTP, and browser transport

`System.Text.Json` options are owned per external seam; there is no application-wide mutable default. Each external-seam options instance is cloned from the .NET 10 `JsonSerializerOptions.Strict` preset, then attaches only that seam's source-generated context and explicit closed converters. The preset rejects unmapped members and duplicate properties, uses case-sensitive names, and enforces nullable annotations and required constructor parameters. Project Format additionally performs the bounded reader pass required by its specification: strict serializer options do not prove token/depth/lexical, ordering, canonical-byte, or policy constraints. Source generation reduces reflection and makes the serializable graph explicit, but it does not replace lexical, policy, or Domain validation ([strict JSON options](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#strict-json-serialization-options), [source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)). JSON DTOs never double as Domain entities or EF entities.

The V1 host has no public JSON API, so it publishes no speculative OpenAPI document and adds no controller/service layer. The closed ordinary HTTP routes remain Minimal API/Razor adapters over typed Application calls. If measurement later creates an independently consumed HTTP interface, define request/response/error schemas first, use typed results and the built-in `AddOpenApi`/`MapOpenApi` support, forward `HttpContext.RequestAborted`, and expose the document only under an explicit authorization/environment policy. ASP.NET Core 10 documents the built-in generator in [OpenAPI support](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0).

All non-success HTTP bodies use the [HTTP Transfer Contract](../contracts/http-transfer.md). Register `AddProblemDetails`; one adapter customizes stable `type`, `code`, correlation, safe localization, and redaction through `IProblemDetailsService`. Endpoint handlers contain no repeated try/catch translation. ASP.NET Core's default implementation and middleware integration are documented under [Problem Details service](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0#problem-details-service).

Browser DTOs are closed values tied to the build fingerprint, not a long-lived public versioned protocol. Interop batches complete semantic values, never pointer samples or core object graphs. Unknown properties/variants, nonfinite numbers, duplicate identities, version mismatch, and oversize data fail before dispatch.

## 7. Observability

Core deterministic Modules return structured Diagnostics and work evidence; they do not log internally or depend on an OpenTelemetry package. Application, Infrastructure, and Web create named BCL instruments at their owned calls:

```text
ActivitySource: LogicLab.Application | LogicLab.Infrastructure | LogicLab.Web
Meter:          LogicLab.Application | LogicLab.Infrastructure | LogicLab.Web
```

Each source/meter is created once and carries the defining assembly's version. Activities cover meaningful admission, queue wait, Module call, repository, transfer, and atomic publication phases. `ActivitySource.StartActivity` may return null when no listener records it, so correctness never depends on an Activity ([distributed tracing instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs#activitysource)).

Metric instruments have stable names, units, descriptions, and low-cardinality dimensions. Identity, source location, Project name/content, policy observed value, Trace value, exception text, URL, and token never become metric tags. Histograms record declared duration or size units; monotonic work counters remain result evidence rather than high-cardinality labels. .NET's library instrumentation entry point is `System.Diagnostics.Metrics.Meter` ([metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#create-a-custom-metric)).

Structured log events use source-generated `LoggerMessage` methods with stable event IDs and templates. Expensive arguments are guarded, and messages never interpolate payloads or secrets. The host wires exporters through OpenTelemetry at the composition root; Modules depend only on BCL diagnostics and `Microsoft.Extensions.Logging` where logging is an owned adapter concern. See [.NET observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) and [logging for library authors](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging-library-authors).

## 8. Performance and publication

Use .NET 10 runtime libraries before custom infrastructure: arrays, spans, bit operations, `System.IO.Compression`, cryptography, `System.Text.Json`, collections, channels, diagnostics, and hosting already cover the baseline. The runtime-library catalog is a discovery map, not permission to add abstraction or optimize without a profile ([runtime libraries overview](https://learn.microsoft.com/en-us/dotnet/standard/runtime-libraries-overview)).

Correct scalar code and full recomputation are the oracle. LINQ is acceptable off hot paths; explicit loops, frozen collections, pooling, SIMD, parallelism, caching, and unsafe code are local measured replacements, not global style rules. BenchmarkDotNet compares production-shaped cases; allocation/CPU/browser/load profiles decide whether a candidate remains. Do not set GC latency mode, server/workstation GC, Tiered PGO, ReadyToRun, ThreadPool minimums, or hardware-intrinsic switches without a deployment profile and end-to-end evidence. Runtime defaults can improve across .NET 10 patches without changing Module semantics.

The first Web deployment is framework-dependent, untrimmed, and JIT-compiled. `PublishTrimmed`, single-file, ReadyToRun, and self-contained are separate publication profiles, not generic “performance” flags. Trimming can break patterns that analysis cannot understand, especially reflection; adopt an alternative profile only after every dependency is compatible, all warnings are resolved rather than suppressed, and startup/RSS/deployment evidence beats the baseline ([publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [trimming warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)). Native AOT is rejected for the V1 Web host rather than merely deferred because ASP.NET Core lists Blazor Server—the server model behind Interactive Server—as unsupported ([ASP.NET Core Native AOT compatibility](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0#review-aspnet-core-and-native-aot-compatibility)). Blazor WebAssembly AOT is a different deployment model and does not optimize the server-owned engine.

Invariant globalization is forbidden because V1 localizes English and Simplified Chinese and shapes Unicode text. A deployment image must carry the required globalization/font assets and pass the culture/browser fixtures. Release artifacts include symbols needed by the declared diagnostics process, a deterministic build fingerprint derived from source and assets rather than wall time, SBOM/provenance from the build system, and the exact runtime/dependency lock evidence.

## 9. Verification and completion gate

The first implementation change establishes these gates before feature breadth:

```text
dotnet restore logic-lab.slnx --locked-mode --nologo
dotnet build logic-lab.slnx --no-restore --nologo
dotnet test --solution logic-lab.slnx --no-build --no-restore --nologo
dotnet format logic-lab.slnx --verify-no-changes --no-restore
git diff --check
```

CI additionally verifies the actual SDK feature band, no project-local package version, lock-file closure, NuGet audit, architecture dependency direction, no production reference to a test project, and no unsafe-enabled project without its evidence record. Tests use xUnit v3 on Microsoft Testing Platform and exercise Module interfaces; local adapters replace real seams. CI declares a test-module parallelism cap appropriate to its agent, while isolated unit/property tests retain xUnit's normal collection parallelism and shared SQLite/server/browser fixtures use explicit isolated or nonparallel collections. FsCheck is consumed directly for semantic properties rather than forcing an xUnit-v2 integration package. bUnit proves Razor projections, Playwright proves browser workflows, and BenchmarkDotNet remains outside `dotnet test`.

This baseline makes implementation reproducible; it does not make a production release qualified. Calibrated policies, provider configuration, migration/backup/restore, browser/security/load evidence, telemetry backend, alerts, and runbooks remain the explicit release work in the [Documentation Map](../README.md#development-readiness).
