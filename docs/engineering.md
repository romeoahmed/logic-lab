# Engineering

> Status: normative repository contract

This document owns engineering rules. [Architecture](./architecture.md) owns module
seams, specifications own behavior, and [Policies](./policies.md) owns limits.
[Executable configuration](./README.md#executable-sources) owns versions and project
shape; [AGENTS.md](../AGENTS.md) owns repository commands and MTP filter syntax.

## Repository baseline

Use the SDK and language selected by the repository. `latest` language versions and
SDK roll-forward are not upgrade policies. Keep nullable analysis, checked arithmetic,
deterministic builds, analyzers, code-style analysis, and warnings as errors enabled.
Any override needs an owning reason at the narrowest applicable scope.

Production projects never reference tests or benchmarks. A new project must have
executable responsibility in the change that introduces it. C# owns production
behavior; browser JavaScript remains a collocated Web adapter.

## Dependencies and restore

- Declare direct versions once in `Directory.Packages.props`; projects use unversioned
  `PackageReference` items. Do not float versions or hide transitive requirements
  behind global overrides.
- Executable roots commit generated lock files. Library lock files do not control
  consumers. Verify restores in locked mode; build and test do not download tools or
  mutable Web assets.
- Prefer the BCL and shared framework. A package must remove material complexity,
  fit the target and license, and justify its transitive graph.
- Keep analyzer and design-time dependencies private, and production packages
  non-packable until an external consumer exists.

See [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management),
[dependency locking](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies),
and [package auditing](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).

## C# modules and interfaces

Types are `internal` unless another production project needs their documented seam.
Use concrete modules where behavior does not vary, and interfaces at real adapter
seams. Avoid generic result wrappers, service locators, repository-per-entity layers,
marker interfaces, and shared `Common` models.

Use records for values and sealed hierarchies for closed variants. Copy referenced
arrays or take exclusive ownership; a record does not make its contents immutable.
Keep spans, pools, mutable builders, EF entities, and browser/JSON records behind
their owner. Validate untrusted input at ingress, then rely on typed invariants.

Expected domain, policy, cancellation, concurrency, and eligibility failures use
closed outcomes. Declare stable reason codes once and translate infrastructure
exceptions at the owning adapter. Hashing, bit packing, modular arithmetic, and
protocol truncation use the smallest necessary `unchecked` scope with boundary evidence.

## Async, concurrency, and time

I/O uses `Task`/`Task<T>` with a final nonoptional `CancellationToken`. CPU modules
stay synchronous and enter Application-owned typed lanes. Domain, Engine, and Razor
handlers do not create hidden queues, block tasks, expose `async void`, or launch
fire-and-forget work.

Cancellation before publication returns the owning cancelled outcome; after commit
it cannot revoke the result. Queues define bounds, ordering, fairness, shutdown, and
full behavior. Locks protect short synchronous invariants and never span `await` or
a module call. Each mutable Simulation Session has one consumer.

Simulation uses Logical Time. Expiry, retention, retry, rate windows, and testable
delays use `TimeProvider`. Capture time once per decision, use elapsed-time APIs for
durations, and express resource lifetimes with `using` or `await using`.

## Dependency injection and configuration

`LogicLab.Web` is the composition root. Use explicit, owner-grouped registrations in
the built-in container; do not call `BuildServiceProvider` or use static service
location. Long-lived Workspaces never capture request or EF scopes. Hosted work
creates a scope per operation.

Bind feature options through `OptionsBuilder`, validate shape and cross-field rules,
and reject invalid startup or readiness. Secrets come from deployment configuration
and never enter source or logs. Reloads publish complete immutable snapshots;
admitted work retains the version required by its contract.

See [.NET dependency injection guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#recommendations).

## Data, transport, and observability

Each external JSON seam owns its serializer options, source-generated context, and
closed converters. Use serializer validation for rules it can express; retain
format-specific lexical, canonical, size, policy, and Domain checks. JSON, browser,
Domain, and EF records have separate ownership.

HTTP and Razor adapt typed Application calls through the
[HTTP contract](./contracts/http-boundary.md). V1 has no public JSON API or speculative
controller layer. Browser messages are build-bound closed records exchanged in
batches; pointer samples and core object graphs never cross interop.

Deterministic modules return diagnostics and evidence without logging dependencies.
Application, Infrastructure, and Web instrument admission, queues, module calls,
repositories, transfers, and publication using low-cardinality metrics. Never record
project content, names, source locations, tokens, URLs, Session IDs, Trace values,
exception text, or unbounded identities.

## Performance and publication

Scalar code and full recomputation define the oracle. Choose LINQ, loops, frozen
collections, pooling, SIMD, parallelism, caching, or unsafe code from measurements.
Benchmarks, browser traces, load tests, counters, and profiles answer different
questions; local means are not product promises.

Keep memory optimizations within their owning module. A pooled buffer has one
rent/use/return scope, is sliced to its logical length, and is returned only after all
consumers stop, including cancellation paths. Published values never retain pooled
views. Clear the full rental when cross-request exposure matters; pooling secrets
requires reviewed lifetimes and zeroization. Stack scratch is initialized, has a
measured byte bound, and is never allocated inside a loop.

Unsafe kernels require a production-shaped profile, an end-to-end gain on each
supported target, and a safe oracle and fallback. Differential tests, fuzzing, and
mutation checks cover bounds, tails, overlap, byte order, and failure cleanup. Enable
unsafe compilation only in the owning project; remeasure after runtime upgrades and
remove paths whose gain disappears. Native interop needs an ADR covering pinning and
cleanup. Binary formats encode fields explicitly instead of copying CLR layouts.
[Memory evidence](./research/dotnet-memory-and-unsafe.md) explains these constraints;
[Module Performance](./research/module-performance.md) owns comparative measurements.

Web publication is framework-dependent, untrimmed, and JIT-compiled. Trimming,
single-file, ReadyToRun, self-contained, Native AOT, GC/ThreadPool tuning, and runtime
switches require a named profile and warning-clean end-to-end evidence. Publish
images with the [.NET SDK container target](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish)
and identify them by OCI digest. Release evidence includes source/asset fingerprints,
dependency locks, SBOM, provenance, diagnostic symbols, and fresh
[component evidence](../conformance/README.md) from the verified commit.

## Verification

Use the centrally pinned TUnit stack on Microsoft Testing Platform with source-generated
discovery. Reflection discovery needs a demonstrated compatibility requirement; do not
mix VSTest runners or coverage adapters. The JIT/MTP suite remains authoritative,
including FsCheck properties; an AOT subset cannot replace it.

| Evidence | Tool and scope |
| --- | --- |
| semantic properties | `TUnit.FsCheck`, independent oracles, shrinking, and replay |
| Razor projection | bUnit in ordinary `.cs` files |
| host seams | `TUnit.AspNetCore`; real Kestrel for HTTPS and chunked-body limits |
| browser behavior | `TUnit.Playwright`, user-facing or stable contract locators |
| comparative performance | BenchmarkDotNet, outside `dotnet test` |

Assert caller-visible behavior and immutable values, not localized prose, allocation
identity, private structure, or predicted performance. Await every TUnit assertion.
Prefer typed values and ordered sequences to boolean reductions. Exact exception and
parameter checks need a documented guard contract; use `Assert.Multiple()` for
independent failures. Coverage and test counts support evidence rather than replace it.

Named cases cover boundaries and regressions, argument tables cover finite cases, and
properties cover broader invariants. Oracles must not call the operation they prove;
generators and shrinkers preserve valid input shape. Custom assertion frameworks,
dynamic discovery, and shared mutable fixtures require a concrete gap in standard tools.

Isolate files, databases, hosts, ports, contexts, cultures, and process state before
adding concurrency constraints. Fixtures own cleanup and pass cancellation through
long-running setup and I/O. HTTPS tests share Kestrel per class; each browser test gets
its own BrowserContext. Shared test hosts admit the independent callers under test;
separate host-security tests verify ingress limits.

Wait for observable asynchronous state with bounded diagnostics. Sleeps, ordering,
repetition, and retries do not repair deterministic failures. Filtered runs fail on an
empty match; test dependencies represent intentional artifact handoffs. The
[testing-platform evidence](./research/testing-platform.md) records platform constraints.

Repository gates establish source health. Provider settings, migrations, recovery,
browser/security/load evidence, telemetry, alerts, and drills remain
[production qualification](./delivery.md#production-qualification).
