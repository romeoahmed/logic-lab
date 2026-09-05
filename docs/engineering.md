# Engineering

> Status: normative repository contract

Executable configuration owns live versions and project shape:

| Fact                                     | Source                                                                                                    |
| ---------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| SDK, roll-forward, test runner           | [`global.json`](../global.json)                                                                           |
| framework, language, analyzers, warnings | [`Directory.Build.props`](../Directory.Build.props)                                                       |
| direct package versions                  | [`Directory.Packages.props`](../Directory.Packages.props)                                                 |
| package sources and audit                | [`NuGet.Config`](../NuGet.Config)                                                                         |
| project graph                            | [`logic-lab.slnx`](../logic-lab.slnx)                                                                     |
| formatting and generated files           | [`.editorconfig`](../.editorconfig), [`.gitattributes`](../.gitattributes), [`.gitignore`](../.gitignore) |

This document owns engineering rules, not inventories. [Architecture](./architecture.md)
owns module seams, focused specifications own behavior, and [Policies](./policies.md)
owns limits.

## Repository baseline

- Use the pinned .NET 10 SDK and C# 14. Do not use `latest` language or SDK
  roll-forward as an implicit upgrade policy.
- Keep nullable analysis, checked arithmetic, deterministic builds, analyzers,
  code-style analysis, and warnings as errors enabled across projects.
- Override shared policy only at the narrowest project or line with an owning reason.
- Production projects never reference tests or benchmarks. Add a project only when
  the same change gives it executable responsibility.
- C# is the sole production language. Browser JavaScript remains a collocated Web
  adapter, not a second domain implementation.

The repository commands and MTP filter syntax live in [AGENTS.md](../AGENTS.md) so
agents and humans share one operational entry point.

## Dependencies and restore

- Declare every direct package version once in `Directory.Packages.props`; project
  files use unversioned `PackageReference` items.
- Do not float versions, hand-edit lock files, or hide a required transitive version
  behind a global override.
- Executable roots commit lock files. Libraries do not claim that a local lock file
  controls their consumers.
- Restore explicitly in locked mode; build and test do not download tools or mutable
  Web assets.
- Prefer the BCL and shared framework. A dependency must remove material complexity,
  fit the target and license, and justify its transitive graph.
- Analyzer and design-time dependencies stay private. Production packages remain
  non-packable until a real external consumer exists.

References: [NuGet Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management),
[dependency locking](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies),
and [package auditing](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).

## C# modules and interfaces

- Types are `internal` unless another production project needs the documented seam.
- Depend on a concrete deep module when behavior does not vary. Add an interface only
  at a real adapter seam.
- Use records and sealed hierarchies only when value or closed-variant semantics are
  real. Copy referenced arrays or take exclusive ownership; records do not make them
  immutable.
- Keep spans, pools, mutable builders, EF entities, and browser/JSON records behind
  their owner.
- Validate untrusted values at ingress, then rely on typed invariants internally.
- Expected domain, policy, cancellation, concurrency, and eligibility conditions use
  closed outcomes. Translate infrastructure exceptions once at the owning adapter.
- Declare stable reason codes once. Do not add generic result wrappers, service
  locators, repository-per-entity layers, marker interfaces, or `Common` models.

Checked arithmetic is the default. Hashing, bit packing, modular arithmetic, and
protocol truncation use the smallest explicit `unchecked` expression with boundary
evidence.

## Async, concurrency, and time

I/O uses `Task`/`Task<T>` with a final nonoptional `CancellationToken`. CPU modules
stay synchronous and enter Application-owned typed lanes. Domain, Engine, and Razor
handlers do not create hidden queues, block tasks, expose `async void`, or launch
fire-and-forget work.

Cancellation is cooperative and atomic: before publication it returns the owning
cancelled outcome; after commit it does not revoke the result. Queues are typed and
bounded with explicit ordering, capacity, fairness, shutdown, and full behavior.
Locks protect small synchronous invariants and never span `await` or a module call.
Each mutable Simulation Session has one consumer.

Simulation uses Logical Time. Expiry, retention, retry, rate windows, and testable
delays use `TimeProvider`. Capture time once per decision, use elapsed-time APIs for
durations, and make resource ownership explicit with `using` or `await using`.

## Dependency injection and configuration

`LogicLab.Web` is the composition root and uses the built-in container. Registrations
are explicit and grouped by owner. Do not call `BuildServiceProvider`, resolve from a
static provider, or hide service location. Long-lived Workspaces never capture request
or EF scopes; hosted work creates an explicit scope per operation.

Feature options bind through `OptionsBuilder`, validate shape and cross-field rules,
and fail startup or readiness when invalid. Secrets come from deployment configuration
and never enter source or logs. Reloadable configuration publishes one complete
immutable snapshot; admitted work retains the version required by its contract.

Reference: [.NET dependency injection guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#recommendations).

## Data, transport, and observability

Every external JSON seam owns its serializer options, source-generated context, and
closed converters. Strict serialization does not replace lexical, size, canonical,
policy, or Domain validation. JSON, browser, Domain, and EF records remain distinct.

V1 has no public JSON API or speculative controller layer. HTTP and Razor adapt typed
Application calls; failures use the [HTTP contract](./contracts/http-boundary.md).
Browser messages are build-bound closed records exchanged in batches; pointer samples
and core object graphs never cross interop.

Deterministic modules return diagnostics and evidence without depending on logging.
Application, Infrastructure, and Web instrument admission, queues, module calls,
repositories, transfers, and publication. Metrics use low-cardinality dimensions.
Never record project content, names, source locations, tokens, URLs, Session IDs,
Trace values, exception text, or unbounded identities.

## Performance and publication

Correct scalar code and full recomputation are the oracle. LINQ, loops, frozen
collections, pooling, SIMD, parallelism, caching, and unsafe code are local choices
backed by measurement. BenchmarkDotNet, browser traces, load tests, counters, and
profiles answer different questions; no local mean becomes a product promise.

Memory optimizations remain inside their owning module. A pooled buffer has one
auditable rent/use/return scope, is sliced to its logical length, and cannot be
returned until every consumer has stopped, including after cancellation. Published
values never retain a pooled view. Clear the entire rental when cross-request data
exposure matters; do not pool secrets without a reviewed lifetime and zeroization
design. Stack scratch uses initialized spans and a measured byte bound, never an
allocation inside a loop.

An unsafe kernel requires a production-shaped profile, an end-to-end gain on each
supported target, and a safe oracle and fallback. Differential tests, fuzzing, and
mutation checks must exercise its relevant bounds, tails, overlap, byte order, and
failure cleanup. Keep unsafe compilation disabled outside the owning project and
recheck the comparison after runtime upgrades; remove a path whose gain disappears.
Native interop requires an ADR covering pinning through completion and cleanup on
every failure path. Binary formats remain field-by-field encodings, never raw CLR
layouts. The [memory evidence](./research/dotnet-memory-and-unsafe.md) explains these
constraints; Engine measurements belong in
[Engine Performance](./research/engine-performance.md).

The Web artifact is framework-dependent, untrimmed, and JIT-compiled. Trimming,
single-file, ReadyToRun, self-contained, Native AOT, GC/ThreadPool tuning, or runtime
switches require a named profile and warning-clean end-to-end evidence. Release images
are published with the .NET SDK container target and identified by OCI digest, not a
mutable tag. See [.NET SDK container publishing](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish).

Release evidence includes source/asset fingerprint, dependency locks, SBOM,
provenance, and the symbols required by diagnostics.

## Verification

- All executable tests use the centrally pinned TUnit stack on Microsoft Testing
  Platform with source-generated discovery; reflection mode needs a demonstrated
  compatibility requirement. Do not mix VSTest runners or coverage adapters into it.
- `TUnit.FsCheck` proves genuine semantic properties with shrinking, replay, and an
  independent oracle; retain named boundary examples.
- bUnit proves Razor projection, TUnit.AspNetCore proves host seams, and
  TUnit.Playwright proves browser behavior. BenchmarkDotNet stays outside `dotnet test`.
- Isolate files, databases, hosts, ports, browser contexts, cultures, and process state
  before adding a keyed concurrency constraint.
- Ordering, sleeps, repeats, and retries never repair deterministic failures.
- Assert caller-visible behavior, not localized prose, private structure, or predicted
  performance. Coverage and test counts are telemetry rather than release gates.

Await every fluent TUnit assertion. Prefer a complete typed value or ordered sequence
to boolean reductions that hide the mismatch. Use exact exception and parameter
checks only for a documented guard contract, and `Assert.Multiple()` only when the
failures are independent. A property's oracle must not call the production operation
it is checking; generators and shrinkers must preserve valid input shape.

Named examples cover regressions and boundaries; argument tables cover finite cases;
properties cover broader invariants. Dynamic discovery, custom assertion frameworks,
and shared mutable fixtures need a concrete gap in the standard tools. Filtered jobs
must fail on an empty match. Poll only real asynchronous observation seams, with a
bounded diagnostic timeout; test dependencies are for intentional artifact handoffs.

Fixtures own cleanup and propagate cancellation into long-running setup and I/O.
Use user-facing or stable contract locators in browser tests, and keep bUnit tests
in ordinary `.cs` files. The JIT/MTP suite remains authoritative;
do not drop FsCheck properties to claim an all-AOT suite. The focused
[testing-platform evidence](./research/testing-platform.md) records these platform
constraints and their sources.

Repository gates prove source health, not production qualification. Provider settings,
migrations, recovery, browser/security/load evidence, telemetry, alerts, and drills
remain [delivery work](./delivery.md#production-qualification).
