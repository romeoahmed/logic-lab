# .NET 10 Engineering Baseline Research

> Status: primary-source evidence and readiness assessment, not a second implementation specification
> Target: .NET 10, C# 14, ASP.NET Core 10, EF Core 10, xUnit v3 on Microsoft Testing Platform
> Sources last checked: 2026-07-30

## 1. Scope and method

This review began with every Markdown document in the repository, then tested the resulting design against primary Microsoft, .NET, NuGet, EF Core, xUnit, and BenchmarkDotNet sources. The two overview pages supplied with the review request were used as discovery maps, not as sufficient evidence by themselves:

- [.NET runtime libraries overview](https://learn.microsoft.com/en-us/dotnet/standard/runtime-libraries-overview)
- [What's new in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)

The authoritative project decisions are now in [.NET Engineering Baseline](../specs/dotnet-engineering.md), [Architecture](../../ARCHITECTURE.md), [Web Host](../specs/web-host.md), the focused specifications, and the seam contracts. This note explains why those decisions are appropriate, records rejected alternatives, and identifies evidence that prose cannot supply.

The assessment uses three different meanings of “ready”:

1. **Specification closure** — an implementer can determine ownership, inputs, outputs, invariants, ordering, and failure behavior without inventing product semantics.
2. **Engineering reproducibility** — executable projects, restore graphs, analyzers, tests, and CI prove that the repository policy works.
3. **Production qualification** — one real deployment profile has calibrated limits, secure provider configuration, migration and recovery procedures, telemetry, and measured acceptance evidence.

Conflating these states either blocks implementation on operational facts that can only be measured later or describes an unimplemented system as production-ready.

## 2. Readiness verdict

Logic Lab is ready to begin narrow implementation slices. It is not ready to claim an implemented build or a production release.

The maintained area-by-area status is the [Development Readiness](../README.md#development-readiness) table. The remaining distance is mostly **implementation and qualification evidence**, not more general architecture: projects, dependency closures, executable tests, calibrated policies, and one provider-specific deployment profile. The first slice should prove one deep path through the existing seams and establish repository gates before feature breadth.

## 3. SDK, target framework, language, and analyzers

### 3.1 Exact SDK and deliberate servicing

`net10.0` is the only appropriate target. Multi-targeting would multiply compatibility and test obligations without another consumer. C# 14 is the language associated with .NET 10; an explicit `14.0` makes the repository policy visible. `latest` and `preview` are unsuitable because their meaning changes with the installed SDK. Microsoft explicitly warns that selecting a language newer than the target framework is unsupported and can create runtime or reference-assembly mismatches ([C# language version configuration](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version)).

The exact SDK pin and dependency lock graph are one reviewed unit. `global.json` requires a full SDK version and supports no wildcard or version range. Microsoft now explicitly recommends `rollForward: disable` when package lock files are used so that the SDK and dependency graph remain in lockstep ([`global.json` roll-forward policies](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#rollforward)). The checked-in `10.0.302`, disabled roll-forward, and `allowPrerelease: false` are therefore the reproducible choice.

Exact pinning does not mean ignoring servicing. .NET 10 is LTS through **2028-11-14**, and Microsoft requires systems to remain current on released patches to qualify for support ([.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)). A dependency-maintenance change should update the SDK pin, restore lock files, build, test, publish, and review the resulting artifacts together. A framework-dependent production host also consumes the installed runtime's current security patch independently of the build SDK.

CI must record the resolved SDK (`dotnet --info`), source revision, RID, and artifact digest. `Deterministic=true` and `ContinuousIntegrationBuild=true` normalize compiler output inputs; they do not by themselves make a complete container, JavaScript asset set, database, or deployment byte-for-byte reproducible.

### 3.2 Analyzer policy

`AnalysisLevel=10-recommended` is a valid compound SDK value: the numeric prefix fixes the analyzer release and `recommended` fixes the enablement mode. `EnforceCodeStyleInBuild=true` adds IDE-style rules to command-line builds. These properties are defined by the [.NET SDK code-analysis properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#code-analysis-properties).

The repository-wide choices—nullable enabled, checked arithmetic, safe code, recommended analyzers, code-style build analysis, deterministic build, and warnings as errors—are suitable defaults. They remain useful only if suppressions are narrow and explained. An analyzer warning should not be globally disabled merely to make the initial scaffold green; either repair the design or record a rule-specific reason and regression evidence.

The .NET 10 JIT includes devirtualization, escape-analysis, stack-allocation, layout, and inlining improvements. These are reasons to keep source code clear and benchmark production shapes, not reasons to imitate generated assembly in advance ([.NET 10 runtime improvements](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/runtime)).

## 4. Restore and software supply chain

### 4.1 Central Package Management and sources

Central Package Management should contain exact versions once, in `Directory.Packages.props`. `CentralPackageVersionOverrideEnabled=false` is important enforcement, not decoration: NuGet otherwise permits a project-local `VersionOverride`. Keeping central transitive pinning disabled avoids silently promoting resolved transitive packages into published dependencies. If a vulnerable or incompatible transitive package must be constrained, promote it to an intentional direct dependency, explain why, and remove it once the upstream graph is repaired ([Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)).

`NuGet.Config` correctly clears inherited machine sources. One package source needs no Package Source Mapping. If a second source is introduced, every direct and transitive package pattern must be mapped before it is enabled; otherwise source choice can depend on timing and cache state ([Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)). A new feed is a provenance and security change, not an incidental restore fix.

Application roots should opt into and commit `packages.lock.json`; common class libraries should not claim that their isolated lock file controls a consuming application's resolved closure. CI must restore with `--locked-mode` and run later build/test steps without restore. This is NuGet's own distinction between executable/application and common-library lock files ([dependency locking](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)).

.NET 10 enables framework package pruning by default for `net10.0`, including pruning eligible direct references. The first lock file created by an implementation slice will therefore already reflect that behavior. Enabling or changing pruning explicitly later can legitimately reduce a lock graph and requires an intentional lock regeneration and review ([`PrunePackageReference`](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#prunepackagereference)).

### 4.2 NuGet Audit severity is easy to misconfigure

For `net10.0`, `NuGetAuditMode` defaults to `all`, so transitive dependencies are audited. `NuGetAuditLevel=low` means report advisories from low upward; it does not decide which severities fail. Warning codes are:

| Code | Meaning |
|---|---|
| `NU1901` | low |
| `NU1902` | moderate |
| `NU1903` | high |
| `NU1904` | critical |
| `NU1905` | an audit source supplies no vulnerability database |

The subtle point is that `TreatWarningsAsErrors=true` also raises NuGet and MSBuild warnings, not just C# diagnostics. To implement the selected policy “observe low; fail moderate, high, and critical,” `NU1901` must be placed in `WarningsNotAsErrors`; merely adding `NU1902`–`NU1904` to `WarningsAsErrors` does not exempt low severity from the global setting. Audit-source failure can remain fatal. Microsoft documents both the defaults and this exact warning-promotion interaction in [NuGet Audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages#configuring-nuget-audit).

An advisory suppression must name the advisory URL, affected path, applicability analysis, owner, and expiry or upgrade issue. `NuGetAuditSuppress` is preferable to disabling audit or suppressing a whole warning code. Restore network failure and lack of vulnerability data must not silently turn a release green.

Exact package versions still belong to the implementation slice that consumes them. Selecting packages in an empty solution would create inventory without evidence and would make the research note stale immediately.

## 5. Runtime libraries, JSON, time, and concurrency

### 5.1 Use the BCL as mechanism, not architecture

The runtime-library catalog confirms that the BCL already supplies collections, spans, bit operations, compression, cryptography, JSON, channels, diagnostics, networking, and hosting primitives. Those primitives should remain private mechanisms behind Logic Lab Modules. A BCL namespace is not a new bounded context or a reason to leak `Span<T>`, pooled owners, `Channel<T>`, JSON DTOs, or EF entities through a Module interface.

.NET 10 improves `ZipArchive` performance and memory use and adds asynchronous ZIP APIs. The strict `.logiclab` reader must still count compressed and decoded bytes, entries, tokens, depth, identities, and policy dimensions itself. Broad extraction APIs and runtime optimizations do not prove the project's ZIP profile or defend against adversarial carriers ([.NET 10 library changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#ziparchive-performance-and-memory-improvements)).

### 5.2 Strict JSON is the correct .NET 10 starting point

.NET 10 adds `JsonSerializerOptions.Strict`. It combines:

- rejection of unmapped members;
- rejection of duplicate properties;
- case-sensitive binding;
- respect for nullable annotations; and
- respect for required constructor parameters.

Each external JSON seam should derive its own immutable options from this preset, then add its source-generated context and closed converters. The preset is stronger and clearer than reconstructing only some of these switches by hand ([strict JSON options](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#strict-json-serialization-options)).

Strict options and source generation still do not prove the Project Document contract. A bounded `Utf8JsonReader` pass remains necessary for token/depth/string/entity limits, lexical integer rules, canonical export order, duplicate identities, discriminator closure, and digest input. DTO validation remains separate from Domain construction. Source generation makes the serializable graph explicit and reduces reflection; it is not semantic validation ([System.Text.Json source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)).

.NET 10 also adds direct `PipeReader` deserialization. It may remove an adapter in an already-pipelined host, but it should be adopted only if the bounded reader and ownership model remain simpler. A new API alone is not evidence to redesign the Project Format seam.

### 5.3 Time, cancellation, and execution lanes

`TimeProvider` is the correct injected mechanism for wall-clock facts, timers, expirations, retention, and tests. `TimeProvider.System` uses UTC wall time and a high-frequency timestamp source; elapsed durations should use `GetTimestamp`/`GetElapsedTime`, not subtraction of two civil timestamps ([TimeProvider overview](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)). Logical Time remains a Simulation value and must never be sourced from `TimeProvider`.

Cancellation is cooperative and owned by an operation. Request abort, explicit user cancellation, supersession, policy timeout, and host shutdown should be linked only where the operation contract says each cause applies. A linked token erases cause identity; the owning adapter must inspect its source tokens and use a declared precedence when simultaneous signals are possible. Every linked or timeout `CancellationTokenSource` is disposed after all consumers terminate ([managed cancellation](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)).

ASP.NET Core request-timeout middleware only cancels `HttpContext.RequestAborted`; it does not automatically abort execution. The default unhandled response is 504, and there is no default timeout policy. Work that Application has already accepted must deliberately detach from request/circuit observation lifetime while retaining its own user, supersession, policy, and shutdown cancellation ([request timeouts](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-10.0)).

`Channel<T>` is a useful mechanism for a bounded lane, but it does not prove newest-wins compilation, per-Session serialization, identity-fair analysis, or atomic shutdown. Those remain Application policies. The bounded channel default full mode is `Wait`; a drop mode is invalid where an admitted atomic command cannot disappear ([channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)).

CPU Modules should remain synchronous and be admitted by the Work Coordinator. Razor handlers and Module implementations must not create hidden `Task.Run` work. Blocking ThreadPool workers with `.Result`, `.Wait()`, or long synchronous I/O can make ASP.NET latency grow while CPU remains underutilized; queue length, thread count, traces, and stacks are the diagnostic evidence ([ThreadPool starvation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-threadpool-starvation)).

## 6. Dependency injection and configuration

The built-in container is sufficient for the present modular monolith. Registrations should be explicit and grouped by owning Module. A production interface is justified by a real variable seam, not by a desire to mock every class. Microsoft's DI guidance rejects service location, async resolution, configuration-time `BuildServiceProvider`, and singleton capture of scoped services ([DI guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)).

Configuration should bind to feature-owned typed options, validate shape and cross-field rules, and use `ValidateOnStart` for required host configuration ([options validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#options-validation)). Raw `IConfiguration` should not travel through Modules.

Reloadable configuration is not automatically reloadable semantics. Policy, library, profile, and security inputs should be built as complete immutable versioned snapshots and atomically published only after validation. An admitted operation retains the snapshot required by its contract. `IOptionsMonitor` can observe a provider change, but its callback must not partially mutate an active Workspace or change the meaning of already admitted work.

Scopes follow operation ownership. A long-lived circuit, Workspace, or hosted worker must not capture a request `DbContext`; hosted work creates an explicit scope, and the SQLite adapter uses one short-lived context per repository operation.

## 7. ASP.NET Core host and observability

The existing host choice—Static SSR for ordinary pages and per-page Interactive Server for the editor—fits the product's server-owned Workspace and browser-local gesture loop. V1 has no independently consumed JSON API, so adding controllers, a public OpenAPI surface, or an API service layer would create a contract without a consumer. If that seam later exists, ASP.NET Core 10's first-party OpenAPI support is preferable to speculative third-party infrastructure ([ASP.NET Core OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)).

HTTP failures should pass through one `IProblemDetailsService` adapter and the closed HTTP Transfer reason codes. Do not repeat exception mapping in every endpoint ([Problem Details service](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0#problem-details-service)).

Production hosting still needs a concrete provider profile:

- persist Data Protection keys beyond a container/process lifetime, restrict access, protect them at rest where required, and keep a stable application discriminator ([Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0));
- accept forwarded scheme, host, and client address only from explicitly trusted proxies/networks; unrestricted forwarded headers permit spoofing ([proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0));
- separate liveness from readiness and return no sensitive dependency details ([health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#separate-readiness-and-liveness-probes)); and
- define public origin, TLS termination, secret provider, cookie policy, key store, database volume, and graceful-shutdown budget together.

Core deterministic Modules should return Diagnostics and work evidence rather than log. Application, Infrastructure, and Web can instrument owned boundaries through `ActivitySource`, `Meter`/`IMeterFactory`, and source-generated `LoggerMessage` methods. OpenTelemetry exporters belong at the composition root; OTLP is a deployment adapter, not a Domain dependency ([.NET observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel), [high-performance logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging)).

Before production qualification, create a stable instrument/event catalog with names, units, low-cardinality dimensions, event IDs, sampling, redaction, and retention. Project, Workspace, Session, Operation, Trace, URL, exception text, and user-authored values must not become metric dimensions. Microsoft explicitly warns that unbounded tag combinations multiply backend storage and cost ([metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)).

## 8. Publication, containers, GC, and globalization

### 8.1 Baseline publication profile

The first Web publication should be **framework-dependent, untrimmed, and JIT-compiled**. This is a deliberate baseline, not an unfinished optimization:

- framework-dependent deployment uses the latest compatible installed runtime security patch;
- self-contained deployment carries a runtime but does not roll to a newly installed security patch, so the artifact must be rebuilt and redeployed for runtime fixes;
- ReadyToRun can reduce startup/first-use JIT work, but assemblies commonly grow two to three times and working set or disk loading can regress;
- single-file can be framework-dependent or self-contained, but some native files require extraction, some APIs depend incorrectly on assembly paths, and compression adds startup decompression cost;
- trimming is only supported for self-contained publishing and can break code whose reflection or dynamic dependencies cannot be statically analyzed; and
- Native AOT is self-contained, requires trimming, forbids dynamic loading patterns, and has reduced diagnostic/dynamic capabilities.

These trade-offs are documented in the [.NET publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run), [single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained), and [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

Native AOT is not merely unproven for Logic Lab's Web host: the ASP.NET Core compatibility table lists **Blazor Server as unsupported**. Interactive Server depends on that server-side model, so Native AOT cannot be the V1 Web baseline ([ASP.NET Core Native AOT compatibility](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0#review-aspnet-core-and-native-aot-compatibility)). Blazor WebAssembly AOT is a different deployment model and would not optimize the server-owned engine.

Any alternative profile must publish frequently in CI, resolve every compatibility warning rather than blanket-suppress it, run the same functional/adversarial suite against the published artifact, and beat the baseline on declared startup, RSS, size, throughput, or operational evidence. Logic Lab is a long-lived interactive process, so startup-only improvements have low priority until measured otherwise.

### 8.2 Runtime and container defaults

Do not pre-tune GC, ThreadPool minimums, Tiered PGO, or hardware intrinsics. Dynamic Adaptation to Application Sizes (DATAS) is enabled by default starting in .NET 9. In a memory-limited container, the GC treats the container limit as physical memory and defaults its heap hard-limit percentage to 75%, with at least a 20 MiB hard limit. That leaves non-GC native, stack, SQLite, compression, browser-circuit, and OS memory outside the managed heap; a container limit therefore must exceed the desired GC heap ([GC runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)).

Record container memory and CPU limits, RID/architecture, base image digest, runtime patch, globalization assets, writable volume, and process user. Tune only from counters, traces, dumps, load tests, and the versioned corpus. Per-operation hard memory isolation cannot be created by an in-process GC setting; if it becomes a requirement, the existing worker-process evidence trigger is the appropriate seam.

Invariant globalization is incompatible with the V1 `en-US`/`zh-CN` host and Unicode text shaping. It removes access to culture-specific data and restricts culture creation; the deployment image must include required ICU and font assets and pass culture/browser fixtures ([globalization runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization)). Stable codes, JSON names, digests, and logical-time decimal forms remain explicitly invariant regardless of UI culture.

## 9. EF Core and SQLite deployment evidence

SQLite fits the initial single-process adapter, but it is not a smaller SQL Server. EF Core's provider documentation records several consequences:

- SQLite has no database-generated concurrency token equivalent to SQL Server `rowversion`; the current-revision pointer needs an application-managed token and an exact `DbUpdateConcurrencyException` to `durable_save_conflict` translation ([EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#application-managed-concurrency-tokens));
- many schema changes require a table rebuild;
- SQLite cannot generate EF idempotent migration scripts because it lacks the required procedural conditional logic; and
- EF's SQLite migration lock can remain abandoned after process termination and block later migration attempts ([SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)).

The Web process should not migrate its production database at startup. A deployment profile must choose a reviewed version-specific SQL script or migration bundle, check for pending model changes in CI, quiesce the process as required, back up before mutation, apply against the expected schema, verify readiness, and document recovery from a failed rebuild or abandoned migration lock. Microsoft recommends inspecting and testing migrations before production and generally prefers reviewed scripts; migration bundles are an alternative when a database tool is unavailable ([applying EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)).

The provider profile must also specify the database path and permissions, volume durability, connection/busy policy, journal/synchronous choice if changed from defaults, backup mechanism, retention, integrity check, restore drill, and maximum supported database size. These are deployment facts. They must not leak into the Project Format or redefine `.logiclab` compatibility.

## 10. Testing, Microsoft Testing Platform, and benchmarks

.NET 10 natively selects Microsoft Testing Platform through `global.json`. The `dotnet test` integration requires MTP 1.7 or later; each test project's xUnit/MTP package graph should be exact and centrally versioned when the project is created ([testing with `dotnet test`](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test), [MTP overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro), [xUnit v3 on MTP](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)). Do not add `Microsoft.NET.Test.Sdk` and a VSTest adapter by habit when the chosen runner and IDE path do not require them.

`dotnet test --solution logic-lab.slnx` is the stable whole-solution entry point after the first test project exists. With the current empty MTP-enabled solution, SDK 10.0.302 exits nonzero with `The solution configuration '|' is invalid`; that empty-baseline behavior must not be described as a successful zero-test run. MTP can run test modules in parallel and defaults the maximum module count to `Environment.ProcessorCount`; CI should set an explicit cap when browser, SQLite, or load fixtures would otherwise oversubscribe the agent ([MTP `dotnet test` options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)).

xUnit's default within an assembly is parallel test collections, normally one per class, using the conservative algorithm. Keep that default for isolated unit/property tests. Put tests that share a database, server, browser, port, static process state, or deployment fixture into an explicit nonparallel collection or give each test an isolated resource. Disabling all parallelism hides ownership defects and makes the semantic suites unnecessarily slow ([xUnit parallel execution](https://xunit.net/docs/running-tests-in-parallel)).

BenchmarkDotNet belongs in a separate executable project and outside `dotnet test`. Run optimized Release builds without an attached debugger, record runtime/OS/hardware/power configuration, use production-shaped corpus cases, and compare on a controlled host. A noisy shared CI timing result is telemetry, not a release gate ([BenchmarkDotNet good practices](https://benchmarkdotnet.org/articles/guides/good-practices.html)). Browser traces and multi-circuit load tests remain separate because a microbenchmark cannot prove circuit capacity, end-to-end latency, or rendering behavior.

## 11. Ordered evidence plan

The shortest route from the documentation baseline to a trustworthy project is:

1. Prove root SDK, analyzer, CPM, audit, locked restore, build, test, format, dependency-direction, and publish policy with the first production/test slice.
2. Implement scalar Module oracles first, then one concurrency-tested SQLite operation and one published Static SSR/Interactive Server vertical slice.
3. Admit packed, pooled, parallel, SIMD, unsafe, or cached paths only as measured differential replacements.
4. Freeze observability and a representative corpus before calibrating policy values.
5. Qualify one provider profile through migration, shutdown, restore, key continuity, patch upgrade, and rollback drills.

## 12. Rejected or deferred choices

| Choice | Decision | Reason |
|---|---|---|
| `latest`/`preview` C# or SDK | reject | changes meaning with installed tooling and weakens reproducibility |
| SDK patch roll-forward with lock files | reject for this repository | official guidance favors exact SDK/dependency lockstep; servicing is an intentional update |
| Multi-targeting | reject for V1 | no second consumer justifies multiplied compatibility evidence |
| project-local package versions or `VersionOverride` | reject | defeats central review and graph ownership |
| central transitive pinning | reject | can silently alter dependency declarations; promote a deliberate direct dependency instead |
| new “Common”/“Runtime” project | reject | creates shallow cross-cutting ownership instead of deep Modules |
| global mutable JSON defaults | reject | external seams have different closed contracts |
| unbounded queues or fire-and-forget tasks | reject | lose admission, identity, cancellation, and shutdown semantics |
| custom DI container, mediator, mapper, generic Result | reject until a measured missing capability exists | adds vocabulary and indirection without a real seam |
| public REST/OpenAPI surface | defer | V1 has no independent API consumer |
| Aspire ServiceDefaults project | defer | observability can be composed directly in the single Web host; another project currently adds no deep seam |
| self-contained, trimming, single-file, or ReadyToRun | separate measured profiles | deployment optimizations have compatibility, size, servicing, and startup trade-offs |
| Native AOT for the Web host | reject for V1 | ASP.NET Core lists Blazor Server as unsupported |
| invariant globalization | reject | incompatible with the localized Unicode product |
| automatic production migration in Web startup | reject | mixes elevated schema mutation with application readiness and weakens review/recovery |
| global test- or benchmark-parallelism disable | reject | isolate shared fixtures and cap only the constrained layer |

## 13. Primary source index

All sources were accessed on 2026-07-30.

### SDK, language, analyzers, and support

- [Select the .NET SDK with `global.json`](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
- [.NET target frameworks](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)
- [Configure the C# language version](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version)
- [.NET SDK MSBuild properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props)
- [.NET and .NET Core support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET 10 runtime changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/runtime)
- [.NET 10 libraries changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries)
- [.NET 10 SDK changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk)

### NuGet

- [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [PackageReference dependency locking and pruning](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
- [NuGet Audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- [Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)

### Runtime libraries and execution

- [System.Text.Json source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [Options pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [Dependency injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)
- [Managed cancellation](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)
- [TimeProvider](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
- [Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [GC runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
- [Globalization runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization)
- [ThreadPool starvation diagnostics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-threadpool-starvation)

### ASP.NET Core, publication, and observability

- [ASP.NET Core request timeouts](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-10.0)
- [ASP.NET Core Native AOT compatibility](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)
- [.NET deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [Trim self-contained deployments](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)
- [ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- [Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [.NET observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [Distributed tracing instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
- [Metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)
- [High-performance logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging)

### Persistence and testing

- [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [EF Core optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Applying EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [Microsoft Testing Platform overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro)
- [Testing with `dotnet test`](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- [`dotnet test` with MTP](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- [xUnit v3 on Microsoft Testing Platform](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [xUnit parallel execution](https://xunit.net/docs/running-tests-in-parallel)
- [BenchmarkDotNet good practices](https://benchmarkdotnet.org/articles/guides/good-practices.html)
