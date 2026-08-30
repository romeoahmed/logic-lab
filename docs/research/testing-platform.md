# .NET Testing Platform Evidence

> Sources reviewed: 2026-08-30
> Scope: Microsoft Testing Platform, TUnit, FsCheck, bUnit, and Playwright
> Authority: evidence for [.NET Engineering](../specs/dotnet-engineering.md); exact versions live in `Directory.Packages.props`

## 1. Project decision

Logic Lab uses one executable-first test stack:

- .NET 10 selects Microsoft Testing Platform (MTP) in `global.json`;
- TUnit supplies discovery, execution, assertions, analyzers, coverage, and TRX support;
- `TUnit.FsCheck` integrates generative properties with TUnit discovery, shrinking, and replay;
- bUnit exercises Razor projections in ordinary `.cs` test files;
- `TUnit.AspNetCore` and `TUnit.Playwright` exercise host and browser workflows.

Do not mix MTP and VSTest in one solution run. Test projects therefore omit `Microsoft.NET.Test.Sdk`, VSTest adapters, xUnit runners, Coverlet, and runner-specific configuration. The centrally pinned package graph and application-root lock files are the executable inventory.

## 2. Project shape and execution

TUnit test projects are executable `Microsoft.NET.Sdk` projects. The `TUnit` package supplies the entry point and common global usings; tests stay in the default source-generated engine mode. Reflection mode is a narrow compatibility exception that requires evidence in the project using it.

The repository commands are:

```shell
dotnet test --solution logic-lab.slnx
dotnet test --solution logic-lab.slnx --treenode-filter "/*/*/ScalarLogicTests/*"
```

In native .NET 10 MTP mode, test options are passed directly. A literal `--` is only for documented .NET CLI binding ambiguity. Do not append `--nologo`: the CLI forwards it to test applications, where it is not a supported option.

Filtered CI jobs must guard against an empty match with MTP's minimum-test option or an equivalent inventory check. A misspelled filter must not produce a green zero-test run.

## 3. Assertions and failure quality

TUnit assertions are asynchronous and do not execute unless awaited. Every assertion-bearing test therefore returns `Task`, and the unawaited-assertion analyzer remains an error.

Prefer assertions that describe the caller-visible contract:

- compare a complete ordered value once instead of asserting each element separately;
- recover the typed subject from an awaited type assertion before inspecting it;
- use exact exception type and parameter assertions only when they are part of the public guard contract;
- use `Assert.Multiple()` only for independent mismatches whose combined report is materially more useful;
- avoid snapshots of localized text, private fields, incidental ordering, or implementation phases.

Retries and repeats do not repair deterministic failures. Polling belongs only at real asynchronous observation seams and must have a bounded, diagnostic timeout.

## 4. Choosing example, matrix, and property tests

| Evidence needed                                                       | Technique                            |
| --------------------------------------------------------------------- | ------------------------------------ |
| one named semantic example or regression                              | ordinary `[Test]`                    |
| a small finite boundary table                                         | argument or method data              |
| an intentional finite cross-product whose rows need separate identity | Matrix data                          |
| an invariant over a large input domain                                | `TUnit.FsCheck` property             |
| host, persistence, or transfer integration                            | real adapter with isolated resources |
| Razor projection                                                      | bUnit                                |
| browser input, Canvas, layout, or end-to-end workflow                 | Playwright                           |

Properties use domain-aware generators, independent oracles, shrinking, and reproducible seeds. Do not turn a handful of examples into a property merely to raise an invocation count. Keep explicit examples for named semantic boundaries even when a broader property also covers them.

Dynamic tests, custom discovery, generated assertion libraries, and shared mutable fixtures require a demonstrated gap in the built-in model. They are not defaults.

## 5. Concurrency and lifecycle

TUnit makes tests eligible for parallel execution by default. Preserve that default for isolated tests:

1. give each test unique files, databases, hosts, ports, browser contexts, and identities;
2. use a keyed `[NotInParallel]` only for an unavoidable shared resource;
3. use a typed parallel limiter for a genuinely bounded pool;
4. use global serialization only when the whole process is the shared resource.

Test ordering never substitutes for isolation. `[DependsOn]` is appropriate only when one test consumes an artifact intentionally produced by another; it is not a way to make a stateful suite pass.

Resource-owning fixtures must have explicit scope and cleanup. Test cancellation flows into long-running setup, browser, host, and data-source work. A timeout is a termination boundary with useful diagnostics, not an estimate of acceptable performance.

## 6. Web evidence boundaries

bUnit proves Razor and Fluent UI projection, event wiring, and component state. It does not prove browser layout, JavaScript interop, Canvas pixels, pointer capture, or network recovery.

Keep bUnit tests in `.cs` files under the ordinary .NET SDK. TUnit and Razor source generators cannot rely on one another's generated output, so Logic Lab does not add Razor test files or switch the project to the Razor SDK.

Playwright tests use resilient user-facing or stable contract locators, start from isolated state, and assert observable outcomes. Browser tests cover primary pointer workflows, shared shortcuts, responsive layouts, reconnect and transfer flows, and precise Canvas evidence where pixels are the behavior. Retries never mask deterministic product defects.

## 7. Qualification boundary

The ordinary JIT/MTP suite is authoritative. TUnit supports source-generated and Native AOT scenarios, but `TUnit.FsCheck` relies on reflection and dynamic code. Logic Lab does not split or weaken semantic property coverage merely to claim an all-AOT test suite.

Coverage and test counts are telemetry, not release gates. Evidence follows ownership: Domain and Engine tests prove semantics, integration tests prove real seams, bUnit proves Razor projection, and Playwright proves browser behavior.

## 8. Primary sources

- [Microsoft Testing Platform overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro)
- [Testing with `dotnet test`](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- [MTP mode in the .NET CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- [TUnit installation](https://tunit.dev/docs/getting-started/installation/)
- [TUnit assertions](https://tunit.dev/docs/assertions/getting-started/)
- [TUnit parallelism](https://tunit.dev/docs/execution/parallelism/)
- [TUnit filters](https://tunit.dev/docs/execution/test-filters/)
- [TUnit FsCheck integration](https://tunit.dev/docs/examples/fscheck/)
- [TUnit Playwright integration](https://tunit.dev/docs/examples/playwright/)
- [bUnit test-project setup](https://bunit.dev/docs/getting-started/create-test-project.html)
