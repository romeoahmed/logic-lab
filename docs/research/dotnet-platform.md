# .NET Platform Evidence

> Sources reviewed: 2026-08-30
> Scope: .NET 10, C# 14, ASP.NET Core, EF Core, dependency management, and publication
> Authority: evidence for [.NET Engineering](../specs/dotnet-engineering.md), [Architecture](../../ARCHITECTURE.md), and [Web Host](../specs/web-host.md)

## 1. Decision summary

Logic Lab is a single-target `net10.0` modular monolith with C# 14, a server-first Blazor Web App, EF Core with SQLite, and the built-in dependency-injection, configuration, logging, diagnostics, JSON, compression, cryptography, rate-limiting, and data-protection facilities.

Executable configuration owns the live inventory:

- `global.json` pins the SDK and selects Microsoft Testing Platform;
- `Directory.Build.props` owns target framework, language, analyzers, and build policy;
- `Directory.Packages.props` owns direct package versions;
- `logic-lab.slnx` owns the project graph;
- application-root lock files own resolved dependency closures.

Research explains why those choices fit. It does not copy their current values.

## 2. SDK, language, and analysis

One target framework avoids multiplying compatibility work without a second consumer. An explicit C# language version prevents `latest` or `preview` from changing meaning with the installed SDK. A disabled SDK roll-forward and reviewed lock graph make the build input reproducible; servicing is an intentional maintenance change, not an ambient machine choice.

Nullable analysis, checked arithmetic, deterministic builds, code-style analysis, .NET analyzers, and warnings as errors are repository defaults. Analyzer findings are design leads, not commands: fix the cause or document one narrow suppression. Do not disable a rule globally merely to clear a build.

The JIT and BCL already optimize common code shapes. Keep source straightforward and measure production workloads before adding pooling, unsafe code, hardware specialization, runtime switches, or duplicate overload families.

## 3. Restore and supply chain

Central Package Management records each direct version once and rejects project-local overrides. A second package source requires complete source mapping before use. Package-source, lock-graph, and vulnerability-policy changes are security-relevant diffs.

Application and executable test roots commit lock files because those files describe a resolved runnable closure. Common libraries do not claim an isolated lock graph controls their consumers. CI restores in locked mode, then builds and tests without changing the graph.

NuGet Audit severity and build severity are separate concerns. With warnings as errors, low-severity audit findings must be exempted explicitly if the policy is “observe low, fail moderate and above.” Suppress an advisory by URL only after recording applicability, owner, and expiry; do not turn off audit or a whole warning family.

## 4. BCL mechanisms and strict data boundaries

BCL types are mechanisms behind project Modules, not architecture. Collections, spans, pools, channels, JSON readers, ZIP archives, EF entities, and browser records remain private to their owners unless a real seam requires them.

.NET 10 `JsonSerializerOptions.Strict` is the external JSON baseline: reject unmapped and duplicate properties, preserve case sensitivity, and honor nullable and required-constructor metadata. Project Format still performs bounded token, depth, string, number, discriminator, identity, and canonical-order validation. Serializer strictness is not Domain validation.

`ZipArchive` parses archives but does not establish a safe carrier policy. The `.logiclab` reader still bounds actual bytes and expansion, enumerates every entry, rejects duplicate and unsafe names, and avoids path extraction APIs.

`TimeProvider` supplies wall-clock time, elapsed-time measurement, timers, and test control. Simulation Logical Time is a separate Domain value. Cancellation remains cooperative and operation-owned; adapters preserve the cause when linking request abort, user cancellation, supersession, timeout, and shutdown.

## 5. Concurrency, DI, and configuration

CPU Modules remain synchronous and run through Application-owned bounded lanes. Razor handlers and Module implementations do not create hidden `Task.Run` work or block on tasks. `Channel<T>` may implement a lane, but queue order, newest-wins behavior, fairness, rejection, and shutdown remain explicit Application policy.

The built-in container is sufficient. Registrations are explicit and grouped by owner; constructors are synchronous; configuration does not call `BuildServiceProvider` or use service location. Hosted work creates an explicit scope, and long-lived Workspaces never capture request-scoped services.

Feature-owned options validate shape and cross-field invariants at startup. Reloadable provider data becomes a complete immutable versioned snapshot before publication; `IOptionsMonitor` does not license partial mutation of admitted work.

## 6. Blazor, persistence, and security

Static SSR serves ordinary pages; the editor opts into per-page Interactive Server. This matches Application-owned Workspaces and a browser-local gesture loop without introducing a WebAssembly engine, custom Hub, public REST surface, or client project.

Long-lived interactive circuits do not own long-lived EF tracking contexts. Persistence uses `IDbContextFactory<T>` and one short-lived context per operation. SQLite lacks a database-generated `rowversion`, so Durable Version is an application-managed concurrency value checked in the same transaction.

Production schema changes are deployment work. SQLite cannot generate idempotent migration scripts and its migration lock can survive an interrupted migration. A qualified profile therefore chooses a reviewed version-specific script or migration bundle, checks pending model changes, backs up before mutation, and documents abandoned-lock recovery. The Web process does not silently acquire elevated schema privileges at startup.

ASP.NET Core Identity, antiforgery, Data Protection, rate limiting, Problem Details, trusted proxy configuration, health checks, and Content Security Policy remain host responsibilities. Resource locators never grant authority, and logs or metric dimensions never contain project payloads, tokens, Trace values, or unbounded identities.

## 7. Publication and operations

The baseline Web artifact is framework-dependent, untrimmed, and JIT-compiled. Blazor Server is not supported by ASP.NET Core Native AOT, and trimming or ReadyToRun would require warning-clean publication plus functional and operational evidence. Startup-only gains have low value for a long-lived interactive process without measurements.

Container limits must leave room outside the managed heap for native runtime state, stacks, SQLite, compression, sockets, and browser circuits. Do not pre-tune GC, ThreadPool, or Tiered PGO. Record counters and traces first, then change one measured constraint.

Invariant globalization is incompatible with the supported cultures and browser text shaping. Deployment images include the required ICU and font assets; stable codes, JSON field names, digests, and Logical Time remain invariant independently of UI culture.

Activities, metrics, and structured logs originate at Application, Infrastructure, and Web boundaries. Exporters are composition-root adapters. A production profile must name the origin, TLS and proxy trust, secret provider, Data Protection store, database volume, runtime image, resource limits, telemetry backend, migration process, backup/restore, and operational owner.

## 8. Testing and performance

[Testing Platform Evidence](./testing-platform.md) owns the TUnit/MTP rationale. BenchmarkDotNet is for comparative synchronous kernels; browser traces and load tests own browser and host capacity. No local microbenchmark becomes a universal latency promise.

## 9. Rejected defaults

| Option | Reason |
|---|---|
| multi-targeting | no second consumer justifies duplicated compatibility evidence |
| alternate DI container or mediator | no missing built-in capability or independent seam |
| public REST/OpenAPI surface | no independent V1 API consumer |
| `.Client` project, Interactive Auto, or browser engine | conflicts with server-owned Workspace and unmeasured download/runtime cost |
| Native AOT Web host | Blazor Server is unsupported |
| automatic production migration at Web startup | weakens review, least privilege, rollback, and provider-specific recovery |
| speculative pooling, unsafe code, or runtime tuning | ownership and performance value are unproved |

## 10. Primary sources

- [`global.json` overview](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
- [.NET SDK code-analysis properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#code-analysis-properties)
- [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [NuGet dependency locking](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)
- [NuGet Audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- [.NET 10 strict JSON](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#strict-json-serialization-options)
- [`TimeProvider`](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)
- [.NET dependency-injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)
- [Blazor with EF Core](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0)
- [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [Applying EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [ASP.NET Core Native AOT compatibility](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)
- [.NET observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
