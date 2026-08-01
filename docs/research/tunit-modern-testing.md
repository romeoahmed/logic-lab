# TUnit Modern Testing Research

> Verified 2026-07-31 (Asia/Shanghai)
> Scope: migration from xUnit v3 to TUnit for Logic Lab, including execution, assertions, data, lifecycle, parallelism, reporting, AOT, and FsCheck
> Authority: TUnit's official documentation, official GitHub repository, NuGet, and Microsoft Testing Platform documentation; normative project choices remain in the repository specifications

This note distinguishes **source fact** from **Logic Lab inference**. The investigation began from TUnit's official [`llms.txt`](https://tunit.dev/llms.txt) index and followed its links to first-party documentation and source. The official repository was inspected at commit [`d0c74ca21e191e02a8fe56a878d4922046ba7b19`](https://github.com/thomhurst/TUnit/tree/d0c74ca21e191e02a8fe56a878d4922046ba7b19), dated 2026-07-31. Package versions must still be resolved through the repository's normal qualification and lock-file process at implementation time.

## 1. Executive conclusion

**Logic Lab inference:** Migrating the existing tests from xUnit v3 to TUnit is technically straightforward and fits the existing .NET 10 Microsoft Testing Platform baseline. It should be a semantic migration, not only an attribute rename:

- replace the xUnit runner package with the `TUnit` meta-package and remove xUnit runner configuration;
- keep the normal test lane in TUnit's default source-generation mode;
- convert assertions to awaited, strongly typed fluent assertions, using chains and `Assert.Multiple()` only where they improve diagnostics;
- promote current hand-driven FsCheck checks to the official `TUnit.FsCheck` executor so shrinking, replay, cancellation, and property metadata participate in the TUnit lifecycle;
- expose intentionally exhaustive input combinations as individual Matrix test cases where that improves failure identity;
- keep isolated unit/property tests parallel and configure a bounded CI-wide maximum, while reserving keyed `[NotInParallel]`, `[ParallelLimiter<T>]`, and shared data sources for real resource constraints;
- use TUnit's test/session artifacts and built-in HTML/TRX/coverage reporting rather than introducing runner-specific infrastructure;
- treat Native AOT as a separate qualification lane, because `TUnit.FsCheck` is explicitly not Native-AOT compatible;
- keep bUnit tests in `.cs` files under the ordinary .NET SDK and TUnit source-generation mode; do not add Razor test files whose generator output TUnit cannot consume.

Do not apply every TUnit feature indiscriminately. `[Retry]`, `[Repeat]`, `[DependsOn]`, global non-parallel execution, dynamic tests, and shared mutable fixtures solve specific problems and would reduce the quality of the current deterministic unit suite if added without evidence.

## 2. Pre-migration Logic Lab baseline

Before the 2026-07-31 migration, the repository had two executable `net10.0` test projects, both using `xunit.v3.mtp-v2` 3.2.2 through central package management. The Engine test project also referenced FsCheck 3.3.4 directly. Both projects set `UseMicrosoftTestingPlatformRunner`, imported `Xunit` globally, and copied `xunit.runner.json` to output.

That C# corpus contained:

- 49 `[Fact]` declarations;
- 12 `[Theory]` declarations;
- 91 `[InlineData]` rows;
- 6 source-level `QuickCheckThrowOnFailure()` call sites across five files; two call sites were shared helpers invoked by five vector-operation tests, so 9 discovered test methods executed FsCheck properties;
- only ordinary `Assert.Equal`, `Assert.Null`, `Assert.Empty`, `Assert.IsType`, and `Assert.Throws` calls;
- no xUnit fixtures, collections, traits, output helper, skip, timeout, or lifecycle interfaces.

**Logic Lab implementation result:** Both projects now use exact centrally pinned `TUnit` 1.63.0; Engine also uses `TUnit.FsCheck` 1.63.0 and direct FsCheck 3.3.4. The nine generative methods are first-class FsCheck properties, ordered collection checks use `CollectionOrdering.Matching`, and a source-generated matrix exposes 112 word-tail combinations independently. Discovery increased from 140 to 252 tests without changing production code or the scalar differential oracles.

## 3. Package and project model

### 3.1 What to reference

**Source fact:** TUnit's official installation guide says to reference the `TUnit` meta-package, use an executable test project, and not reference `Microsoft.NET.Test.Sdk`, because the latter interferes with discovery. The meta-package includes the TUnit engine and assertions plus Microsoft Testing Platform coverage, TRX, and telemetry extensions ([installation](https://tunit.dev/docs/getting-started/installation), [built-in extensions](https://tunit.dev/docs/extending/built-in-extensions)).

**Source fact:** The current package build imports properties that set `IsTestProject`, `IsTestingPlatformApplication`, `TestingPlatformDotnetTestSupport`, `OutputType=Exe`, and Testing Platform protocol properties. It also enables TUnit and assertion implicit usings and defaults an otherwise unspecified C# language version to `latest` because assertion overload resolution relies on C# 13's `OverloadResolutionPriority` ([`TUnit.props`](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit/TUnit.props), [`TUnit.Engine.props`](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.Engine/TUnit.Engine.props)). Logic Lab already fixes C# 14, so this requirement is satisfied without relying on the package default.

**Source fact:** The official xUnit migration guide provides a Roslyn analyzer/code fixer under diagnostic `TUXU0001`. It converts common facts, theories, inline/member data, assertions, and method signatures, but calls out manual work for custom member-data shapes, fixtures, collection definitions, and ambiguous assertions. The diagnostic is information-level; `dotnet format analyzers --severity info --diagnostics TUXU0001` is the documented invocation ([xUnit migration](https://tunit.dev/docs/migration/xunit)).

**Source fact:** NuGet's live flat-container indexes listed `TUnit` 1.63.0 and `TUnit.FsCheck` 1.63.0 as their latest stable versions when inspected on 2026-07-31 ([TUnit version index](https://api.nuget.org/v3-flatcontainer/tunit/index.json), [TUnit.FsCheck version index](https://api.nuget.org/v3-flatcontainer/tunit.fscheck/index.json), [TUnit 1.63.0](https://www.nuget.org/packages/TUnit/1.63.0), [TUnit.FsCheck 1.63.0](https://www.nuget.org/packages/TUnit.FsCheck/1.63.0)). Search-indexed NuGet pages still advertised 1.61.38, so the live package index is the controlling version evidence. TUnit targets .NET Standard 2.0 and .NET 8 or later, with higher frameworks computed compatible ([official owner profile](https://www.nuget.org/profiles/thomhurst)). The official migration fixer requires .NET SDK 8 or later; Logic Lab's pinned .NET 10 SDK exceeds that floor ([xUnit migration prerequisites](https://tunit.dev/docs/migration/xunit#prerequisites)).

**Logic Lab inference:** At implementation time:

1. add centrally pinned `TUnit` and `TUnit.FsCheck` versions in `Directory.Packages.props`;
2. keep `FsCheck` centrally pinned while Logic Lab test source directly consumes its `Gen`, `Arbitrary`, `Property`, and fluent APIs;
3. replace `xunit.v3.mtp-v2` references with `TUnit`, and add `TUnit.FsCheck` only to the Engine test project;
4. remove global `Xunit` usings, `xunit.runner.json`, and its content items;
5. remove the redundant `UseMicrosoftTestingPlatformRunner` property after verifying TUnit's imported project properties in the resolved package;
6. do not add `Microsoft.NET.Test.Sdk`, Coverlet, or direct Microsoft Testing Platform package versions;
7. regenerate application-root lock files under locked restore and confirm that all transitive Microsoft Testing Platform extensions are intentional.

The package source currently depends on Microsoft Testing Platform 2.3.3, but Logic Lab should consume that transitively from the qualified TUnit release instead of pinning an internal implementation dependency ([TUnit central versions](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/Directory.Packages.props), [`TUnit.Engine.csproj`](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.Engine/TUnit.Engine.csproj)).

### 3.2 Migration mechanics

**Logic Lab inference:** The official fixer is useful as a mechanical first pass, but its output must not define the final test design. A safe implementation sequence is:

1. record the current build, list, and execution result, including the 140 discovered invocations;
2. add TUnit temporarily with its implicit usings disabled as documented, run the `TUXU0001` fixer, and review every diff;
3. manually reshape data/property tests and assertions according to Sections 5 and 6;
4. remove xUnit and the temporary implicit-using suppression;
5. restore, build, list, execute, format, and compare semantic coverage before changing the authoritative test policy.

The official migration guide warns that multi-targeted projects must select one framework when applying the fixer. Logic Lab currently single-targets `net10.0`, so that Roslyn linked-file failure mode does not apply ([multi-targeting warning](https://tunit.dev/docs/migration/xunit#automated-migration-with-code-fixers)).

## 4. Microsoft Testing Platform and execution modes

### 4.1 Native application model

**Source fact:** TUnit is built directly on Microsoft Testing Platform. Tests can run through `dotnet run`, `dotnet test`, `dotnet exec`, the built DLL, or a published executable; `dotnet test` supports projects and solutions ([running tests](https://tunit.dev/docs/getting-started/running-your-tests)). Microsoft describes Testing Platform as an embedded test host and explicitly supports executable test projects and `dotnet test` integration ([Microsoft Testing Platform overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro)).

**Source fact:** .NET 10 introduced native MTP mode for `dotnet test`, selected through `global.json`. In this mode test and extension options are accepted directly as extensible trailing arguments, and the extra `--` separator used by the older VSTest-mediated compatibility mode is no longer used ([`dotnet test` with MTP](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp), [VSTest-to-MTP migration](https://learn.microsoft.com/en-us/dotnet/core/testing/migrating-vstest-microsoft-testing-platform#opt-in-to-mtp-mode-on-net-10-sdk-and-later)). Some TUnit pages still contain older-SDK examples with `--`; the TUnit filter and CI pages separately acknowledge direct .NET 10 syntax ([filters](https://tunit.dev/docs/execution/test-filters), [CI note](https://tunit.dev/docs/examples/tunit-ci-pipeline#filter-tests-by-category)).

**Logic Lab inference:** Preserve `dotnet test --solution logic-lab.slnx` as the whole-repository gate because `global.json` selects native MTP on the pinned .NET 10 SDK. Pass TUnit/MTP options directly, for example `dotnet test --solution logic-lab.slnx --maximum-parallel-tests 4`; do not add the legacy extra `--`. Use `dotnet run --project <test-project> -- <TUnit options>` only for focused single-project diagnosis where `dotnet run` itself needs an application-argument separator. Continue the repository rule that `--nologo` is not passed through the solution test command because native MTP treats extensible trailing options as test-application arguments.

### 4.2 Source generation is the default; Native AOT is additional

**Source fact:** TUnit's default mode source-generates test discovery, invocation delegates, data-source factories, property setters, and hooks. Reflection mode is opt-in through `--reflection`, `[assembly: ReflectionMode]`, or `TUNIT_EXECUTION_MODE=reflection`. Source generation is used in ordinary JIT builds too; publishing with `PublishAot=true` adds Native AOT startup, size, and runtime benefits ([engine modes](https://tunit.dev/docs/execution/engine-modes)).

**Source fact:** TUnit analyzers validate test configuration and data sources at compile time. Generic test methods/classes require explicit instantiations, and AOT-friendly member data is static and compile-time resolvable. Static async sources can return `IAsyncEnumerable<T>` with cancellation ([AOT and generic tests](https://tunit.dev/docs/writing-tests/aot), [method data](https://tunit.dev/docs/writing-tests/method-data-source)).

**Source fact:** Dynamic tests use `DynamicTestBuilderContext` and expression/reflection-based runtime registration. The underlying API is marked as requiring unreferenced code, so dynamic tests are not a Native-AOT-safe replacement for ordinary source-generated tests ([dynamic-test documentation](https://tunit.dev/docs/extending/dynamic-tests), [`DynamicTestBuilderContext` source](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.Core/DynamicTestBuilderContext.cs)).

**Logic Lab inference:** Keep normal Domain and Engine tests in the default source-generation mode; do not add `[ReflectionMode]`. Do not add dynamic tests for truth tables or word-boundary matrices because built-in data sources remain visible, analyzable, filterable, and AOT-friendly.

### 4.3 bUnit and future UI tests

**Source fact:** TUnit documents reflection mode as an opt-in path for tests generated by another source generator. The current bUnit TUnit setup guidance adds the sharper Razor boundary: TUnit and `Microsoft.NET.Sdk.Razor` both use source generators, one generator cannot consume another generator's output, Razor test files do not work with TUnit, and C# test files do ([TUnit engine modes](https://tunit.dev/docs/execution/engine-modes#reflection-mode), [bUnit test-project setup](https://bunit.dev/docs/getting-started/create-test-project.html)).

**Logic Lab inference:** Keep `LogicLab.Web.Tests` as a separate ordinary `Microsoft.NET.Sdk` executable, author every bUnit test in `.cs`, and retain TUnit's default source-generation mode. Do not add `[assembly: ReflectionMode]`, `EnableTUnitSourceGeneration=false`, the Razor SDK, or `.razor` tests to this project. Future Playwright tests can use the official `TUnit.Playwright` integration without changing semantic ownership ([Playwright integration](https://tunit.dev/docs/examples/playwright)).

### 4.4 AOT qualification boundary

**Source fact:** The default TUnit engine is designed for trimming and Native AOT, but `TUnit.FsCheck` is explicitly not compatible with Native AOT because FsCheck uses reflection and dynamic code generation ([FsCheck limitation](https://tunit.dev/docs/examples/fscheck#limitations)).

**Logic Lab inference:** “Uses source generation” is a valid initial migration outcome; “the complete Logic Lab suite publishes under Native AOT” is not. Keep the ordinary JIT/MTP suite authoritative. If Native-AOT test startup becomes a measured CI need, either:

- create an AOT-only deterministic smoke project with no FsCheck or reflection-dependent UI tooling; or
- split deterministic and property tests only if the additional project seam pays for itself.

Never drop semantic property testing merely to claim Native AOT compatibility.

## 5. Assertion model

### 5.1 Await is execution, not decoration

**Source fact:** `Assert.That(...)` constructs an assertion chain; awaiting it executes the assertion and avoids sync-over-async for async delegates. Without `await`, the assertion does not run and a test can pass incorrectly. TUnit ships an analyzer for unawaited assertions, and ordinary assertion-bearing tests therefore return `Task` ([awaiting assertions](https://tunit.dev/docs/assertions/awaiting), [assertion basics](https://tunit.dev/docs/assertions/getting-started)).

**Logic Lab inference:** Convert all migrated assertion-bearing methods to `async Task` and treat any unawaited-assertion analyzer warning as an error under the existing warnings-as-errors policy. Do not preserve xUnit's synchronous assertion shape merely to minimize the diff.

### 5.2 Strong typing and value recovery

**Source fact:** TUnit rejects incompatible equality operands at compile time where possible. Runtime type assertions distinguish exact type (`IsTypeOf<T>`) from assignability and return the validated typed subject when awaited. Collection assertions include ordered equality, equivalence, predicates, count, single-item extraction, and returned matching items ([type checking](https://tunit.dev/docs/assertions/type-checking), [collections](https://tunit.dev/docs/assertions/collections), [await return values](https://tunit.dev/docs/assertions/awaiting#using-return-values-from-awaited-assertions)).

**Logic Lab inference:** Replace the current `Assert.IsType<ComponentContractSchema>` helper with `var schema = await Assert.That(contract).IsTypeOf<ComponentContractSchema>()`. Prefer one sequence assertion over an awaited assertion per bit where the whole ordered vector is the contract. This both preserves bit order and avoids turning a tight differential check into hundreds of async assertion operations.

### 5.3 Chaining and multiple failures

**Source fact:** `.And` requires all conditions on one subject; `.Or` accepts any condition. Mixing `.And` and `.Or` in one chain is unsupported and throws `MixedAndOrAssertionsException`. `Assert.Multiple()` is a disposable assertion scope that collects independent failures and throws one aggregate when the scope exits ([combining assertions](https://tunit.dev/docs/assertions/combining-assertions)).

**Logic Lab inference:** Apply these deliberately:

- use `.And` for several invariants of the same returned object or scalar;
- use `.Or` only for a genuine closed set of allowed values;
- use `Assert.Multiple()` for contract-schema projections where seeing all mismatched IDs, directions, widths, and parameter facts in one run is materially useful;
- keep fail-fast assertions where later work is unsafe after a missing/null/type precondition;
- never mix `.And` and `.Or` in one chain.

### 5.4 Exceptions, tasks, and eventual conditions

**Source fact:** Fluent exception assertions support derived or exact types, messages, parameter names, inner exceptions, synchronous and async delegates, and returning the exception for further checks. TUnit also retains static `Assert.Throws`/`ThrowsAsync` helpers. Task assertions cover completion, cancellation, fault, completion within a duration, and `WaitsFor`/`Eventually` polling with nested assertions ([exception assertions](https://tunit.dev/docs/assertions/exceptions), [task assertions](https://tunit.dev/docs/assertions/tasks-and-async)).

**Logic Lab inference:** Prefer fluent exception assertions such as `ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName(...)` when the exact public guard contract matters. Use `CompletesWithin` or `Eventually` for future asynchronous Application/browser observations; do not replace deterministic Engine calls with polling.

### 5.5 Generated domain assertions

**Source fact:** `[GenerateAssertion]` can generate a complete chainable custom assertion from a `bool`, `AssertionResult`, or async helper; `[AssertionFrom<T>]` can wrap existing methods. The source generator supplies expectation text, caller expressions, `.And`, and `.Or` integration ([source-generated assertions](https://tunit.dev/docs/assertions/extensibility/source-generator-assertions)).

**Logic Lab inference:** Do not create custom assertions during the mechanical migration. If later slices repeat a stable domain projection such as “Compilation has exactly these diagnostics” or “Logic Vector matches scalar oracle,” prefer a small source-generated test-only assertion over copy-pasted loops. It must remain test infrastructure and must not become a second owner of production semantics.

## 6. Data-driven tests and generators

### 6.1 Selection guide

TUnit's official data guide separates the mechanisms as follows ([data approach](https://tunit.dev/docs/writing-tests/data-driven-overview)):

| Need | TUnit mechanism | Logic Lab use |
|---|---|---|
| compile-time constant row | `[Arguments(...)]` | replace each current `[InlineData]` row |
| computed/complex row | static `[MethodDataSource]` | explicit truth tables or rich expected records |
| managed object/fixture | `[ClassDataSource<T>]` | future expensive server/database/browser fixture |
| reusable row metadata | `TestDataRow<T>` | named/skipped/categorized generated rows |
| Cartesian product of parameter values | `[MatrixDataSource]` plus `[Matrix]`, `[MatrixRange]`, or `[MatrixMethod]` | word widths × four-state input combinations |
| Cartesian product of heterogeneous sources | `[CombinedDataSources]` | future bounded cross-product only when sources genuinely differ |
| reusable custom generator | `DataSourceGeneratorAttribute<T...>` | only for a recurring domain corpus that built-ins cannot express |
| huge discovery data | `DeferEnumeration=true` | future corpora where IDE enumeration cost is measured |

### 6.2 Arguments and row metadata

**Source fact:** Each `[Arguments]` supplies one compile-time-known test row. A row can set `DisplayName`, categories, and a skip reason; display names substitute named or positional arguments ([arguments](https://tunit.dev/docs/writing-tests/arguments)). For method/class/custom sources, `TestDataRow<T>` adds equivalent display, skip, and category metadata while preserving a typed payload ([test data rows](https://tunit.dev/docs/writing-tests/test-data-row)).

**Logic Lab inference:** Convert existing `InlineData` rows one-for-one first. Preserve explicit expected values for scalar truth tables rather than computing expected output with the system under test. Add row display names only where the generated name does not already identify `0`, `1`, `X`, `Z`, width, and expected result clearly.

### 6.3 Method data

**Source fact:** Static `[MethodDataSource]` is the AOT-compatible form. It supports typed single values, tuples, factories such as `Func<T>` to create a fresh reference per invocation, and cancellation-aware `IAsyncEnumerable<T>`. Instance member sources are evaluated during discovery before `IAsyncInitializer`, which can silently yield no cases if data depends on execution-time initialization ([method data](https://tunit.dev/docs/writing-tests/method-data-source)).

**Logic Lab inference:** Use static typed tuples for any migrated table too large for readable attributes. Never load project files, databases, or external services in discovery for the current unit suite. If future corpus enumeration is expensive, make the corpus stable and local, then qualify `DeferEnumeration=true` rather than hiding I/O in discovery.

### 6.4 Matrix and combinatorial coverage

**Source fact:** `[MatrixDataSource]` generates the Cartesian product of per-parameter `[Matrix]`, `[MatrixRange<T>]`, or `[MatrixMethod]` values. `[MatrixExclusion]` removes named invalid combinations. This is TUnit's equivalent of combinatorial testing; there is no separate TUnit `[Combinatorial]` attribute ([matrix tests](https://tunit.dev/docs/writing-tests/matrix-tests), [attribute comparison](https://tunit.dev/docs/comparison/attributes)).

**Source fact:** `[CombinedDataSources]` also generates a Cartesian product but accepts any parameter-level TUnit data source. It is AOT-compatible, but official guidance warns about multiplicative test growth ([combined sources](https://tunit.dev/docs/writing-tests/combined-data-source)).

**Logic Lab inference:** Refactor the private vector tail nested loops into individually reported Matrix cases where appropriate: boundary width × left logic value × right logic value. Keep the scalar implementation as the independent expected oracle. This improves test identity, filtering, and parallel scheduling without weakening semantics. Do not turn every hand-curated truth table into a matrix if doing so would erase an explicit expected value or explode the case count.

### 6.5 Custom and dynamic generation

**Source fact:** Strongly typed `DataSourceGeneratorAttribute<T...>` returns factories for rows; asynchronous and untyped variants exist. Async generator code runs during discovery, so expensive or failure-prone I/O can delay or break listing before tests start ([data-source generators](https://tunit.dev/docs/extending/data-source-generators)). Dynamic tests can register tests at discovery or during another test, but are a reflection-oriented escape hatch rather than a normal data mechanism ([dynamic tests](https://tunit.dev/docs/extending/dynamic-tests)).

**Logic Lab inference:** Prefer built-in Arguments, MethodDataSource, Matrix, and FsCheck. A custom generator is justified only if a reusable Logic Lab corpus has stable generation rules and materially improves names/metadata. Dynamic tests are not justified by the current suite.

### 6.6 Discovery deferral

**Source fact:** `DeferEnumeration=true` keeps a large data source as one placeholder node during discovery and expands cases only during execution. It reduces IDE/discovery overhead at the cost of individual pre-run visibility and some filtering; if any source on a test defers, the whole test expansion is deferred ([deferred enumeration](https://tunit.dev/docs/writing-tests/defer-enumeration)).

**Logic Lab inference:** Do not defer current rows: the corpus is small, and individual case visibility is valuable. Reserve it for future large imported/compiler corpus suites after measuring discovery cost.

## 7. FsCheck integration

**Source fact:** The official `TUnit.FsCheck` package supplies `[FsCheckProperty]`, used together with `[Test]`. It supports properties returning `bool`, `void`, `Task`, `ValueTask`, or FsCheck `Property`; exposes `MaxTest`, `MaxFail`, size, replay seed, verbosity, success output, and custom arbitrary configuration; and reports replay seeds and shrunk failures ([TUnit FsCheck](https://tunit.dev/docs/examples/fscheck)).

**Source fact:** The integration installs a custom TUnit test executor during registration and generates FsCheck data during execution, not as discovery-time rows. Its source injects TUnit's timeout-backed `CancellationToken` and explicitly suppresses trimming/AOT diagnostics because FsCheck requires reflection and dynamic code ([`FsCheckPropertyAttribute`](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.FsCheck/FsCheckPropertyAttribute.cs), [`FsCheckPropertyTestExecutor`](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.FsCheck/FsCheckPropertyTestExecutor.cs)).

**Logic Lab inference:** Convert each current `QuickCheckThrowOnFailure` wrapper into a first-class `[Test, FsCheckProperty]` property where practical:

- accept the generated arrays as method parameters instead of nesting `Prop.ForAll` inside a fact;
- return `bool` or `Property` for semantic checks so FsCheck owns shrink/replay reporting;
- preserve `LogicVectorTestData.PositiveWidth`, value creation, scalar oracle, and all boundary-specific deterministic tests;
- set an explicit `MaxTest` only where the present default/QuickCheck configuration differs or evidence requires more coverage;
- let TUnit supply a timeout-linked `CancellationToken` for async properties;
- store a failing replay seed only as a temporary regression aid; materialize a stable minimal example as an ordinary deterministic test when it represents an important bug.

Because FsCheck invocations are generated inside one TUnit test execution rather than one discovery node per random input, filtering and reports identify the property, while FsCheck's failure text identifies the seed and shrunk counterexample. That is appropriate for randomized semantic evidence.

## 8. Parallelism, ordering, and dependencies

### 8.1 Default and global cap

**Source fact:** Every unconstrained TUnit test is eligible to execute concurrently. `[NotInParallel]` without a key runs completely alone; keyed constraints prevent overlap only among tests sharing a key. `[ParallelGroup]` makes isolated phases whose members run together. `[ParallelLimiter<T>]` caps concurrency among tests using the same limiter type. Method attributes override class and assembly attributes ([parallelism](https://tunit.dev/docs/execution/parallelism)).

**Source fact:** A global maximum can be set by `--maximum-parallel-tests`, `TUNIT_MAX_PARALLEL_TESTS`, or `context.Settings.Parallelism.MaximumParallelTests` in `[Before(TestDiscovery)]`. The current implementation defaults the global scheduler to four times `Environment.ProcessorCount`; this is more precise than the parallelism page's broad statement that the thread pool determines execution ([programmatic settings](https://tunit.dev/docs/reference/programmatic-configuration), [`TestScheduler.GetMaxParallelism`](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.Engine/Scheduling/TestScheduler.cs#L508-L570)).

**Logic Lab inference:** Preserve per-test parallel eligibility for the pure Domain and Engine suites. Implement the repository's existing CI cap with either a version-controlled discovery hook or `TUNIT_MAX_PARALLEL_TESTS`; allow the CLI to override it on a deliberate run. Prefer keyed constraints or resource-specific limiters over assembly-wide serialization when database, server, browser, solver, or container tests arrive.

### 8.2 Ordering and DependsOn

**Source fact:** `Order` matters only among tests sharing a non-parallel constraint. `[DependsOn]` prevents a dependent test from starting until named dependencies finish while unrelated tests remain parallel. A failed dependency skips dependents unless `ProceedOnFailure=true`; dependency contexts and state bags can be retrieved, including all invocations of a data-driven dependency. TUnit itself warns that independent tests remain the preferred design ([ordering and dependencies](https://tunit.dev/docs/writing-tests/ordering)).

**Source fact:** Combining `[DependsOn]` with different parallel groups or limiter configurations can make ordering unsupported, so dependency and scheduling constraints must be designed together ([parallelism caveats](https://tunit.dev/docs/execution/parallelism#caveats)).

**Logic Lab inference:** Do not introduce `[DependsOn]` into current semantic unit tests. Use it only for an inherently stateful future end-to-end workflow that cannot afford independent setup, and never use one test as another test's semantic oracle. Shared setup belongs in a fixture/data source, not a predecessor test.

## 9. Lifecycle, injection, and extension points

### 9.1 Test instances and hooks

**Source fact:** TUnit creates a new test-class instance per test invocation. Constructor setup is therefore per test. Async or scoped setup uses `[Before]`/`[After]` at test, class, assembly, test-session, or discovery level; `BeforeEvery`/`AfterEvery` apply globally. Test hooks are instance methods, while class and broader hooks are static. After hooks and disposal still run after failures, and cleanup exceptions are collected ([pitfalls](https://tunit.dev/docs/guides/best-practices), [hooks](https://tunit.dev/docs/writing-tests/hooks), [lifecycle](https://tunit.dev/docs/writing-tests/lifecycle)).

**Logic Lab inference:** Current tests need no hooks. A small `[Before(TestDiscovery)]` global hook is justified only for version-controlled TUnit settings. Future resource setup should use the narrowest lifecycle with cancellation; global hooks must live in a clearly named test-infrastructure file because they affect every suite.

### 9.2 ClassDataSource and resource ownership

**Source fact:** `[ClassDataSource<T>]` injects a managed object and can create one instance per injection, class, assembly, test session, or key. `IAsyncInitializer` and `IAsyncDisposable` provide automatic setup and teardown at the sharing boundary. Shared objects can be used concurrently, so mutable shared state is unsafe without its own synchronization or non-parallel constraint ([class data source](https://tunit.dev/docs/writing-tests/class-data-source), [lifecycle disposal](https://tunit.dev/docs/writing-tests/lifecycle#disposal-and-cleanup)).

**Source fact:** `ClassDataSource<T>` injects one instance; it does not enumerate `T` into rows. Per-row data belongs in MethodDataSource or a custom generator ([generic attributes](https://tunit.dev/docs/writing-tests/generic-attributes#classdatasourceattributet)).

**Logic Lab inference:** Use `PerTestSession` only for genuinely expensive, concurrency-safe resources such as a container image or shared browser installation. Prefer per-test/per-class resources for mutable SQLite databases and server state. Couple any shared resource with isolated names from `TestContext` or an explicit keyed constraint.

### 9.3 Property injection and DI

**Source fact:** TUnit can source-generate required property injection, recursively build nested data-source graphs, initialize dependencies in order, and dispose in reverse. `IAsyncInitializer` runs during execution, after discovery; `IAsyncDiscoveryInitializer` exists only for data required to enumerate cases and should remain cheap because IDEs repeatedly discover tests ([property injection](https://tunit.dev/docs/writing-tests/property-injection)).

**Source fact:** TUnit does not expose its internal provider as application DI. A custom `IClassConstructor` controls construction directly; `DependencyInjectionDataSourceAttribute<TScope>` integrates an external DI container and manages a scope per test-class instance ([dependency injection](https://tunit.dev/docs/writing-tests/dependency-injection), [TestContext DI boundary](https://tunit.dev/docs/writing-tests/test-context#dependency-injection)).

**Logic Lab inference:** Local adapters remain preferable for unit tests. Use TUnit DI integration only for an integration test whose subject is the real composition root; do not turn every unit test into a service-provider resolution exercise.

### 9.4 Event receivers

**Source fact:** Test classes, custom constructors, injected arguments, and attributes can implement lifecycle receiver interfaces covering registration, first/last test at session/assembly/class scope, test start/end, skip, and retry. Start/end receivers choose `Early` or `Late` relative to instance hooks ([event subscribing](https://tunit.dev/docs/writing-tests/event-subscribing), [extension points](https://tunit.dev/docs/extending/extension-points)).

**Logic Lab inference:** Event receivers are appropriate for reusable cross-cutting test infrastructure such as per-test tracing, artifact capture, or scoped resource disposal. They are not a replacement for readable Arrange/Act/Assert code. Prefer an attribute-backed receiver when behavior must be opt-in and carries test metadata.

## 10. Timeouts, retries, repeats, skip, and metadata

### 10.1 Timeouts and cancellation

**Source fact:** `[Timeout(milliseconds)]` applies to a method, class, base class, or assembly. When exceeded, TUnit fails the test and cancels the test's injected `CancellationToken`; operations must observe that token to stop. Method settings override class and assembly settings. Each retry receives a fresh timeout window ([timeouts](https://tunit.dev/docs/execution/timeouts)). Programmatic defaults currently document 30 minutes per test and five minutes per hook unless overridden ([settings](https://tunit.dev/docs/reference/programmatic-configuration#contextsettingstimeouts)).

**Logic Lab inference:** Establish a bounded repository default during implementation and pass the injected token through future async I/O. Keep a separate outer CI/job timeout. A timeout does not make non-cooperative blocking code safe.

### 10.2 Retry and repeat

**Source fact:** `[Retry(N)]` makes up to N additional attempts after failure and can be subclassed with `ShouldRetry` for specific transient exceptions. `[Repeat(N)]` always creates N additional invocations, regardless of outcome; repeats are separate explorer nodes. Method policies override class and assembly policies ([retry](https://tunit.dev/docs/execution/retrying), [repeat](https://tunit.dev/docs/execution/repeating)).

**Logic Lab inference:** Never apply retry globally or to deterministic Domain/Engine/FsCheck failures; that would hide real defects and randomize evidence. Use a custom transient-only retry, if at all, around a qualified external dependency. Use Repeat only in an explicitly categorized stress/consistency test, not as a substitute for FsCheck or a deterministic boundary matrix.

### 10.3 Skip and explicit tests

**Source fact:** `[Skip(reason)]` supports method/class/assembly scope and custom conditional logic. `Skip.Test(reason)` performs a runtime skip from a test or hook. `[Explicit]` excludes a test from general runs and executes it only when the filter selects an all-explicit set ([skip](https://tunit.dev/docs/writing-tests/skip), [explicit tests](https://tunit.dev/docs/writing-tests/explicit)).

**Logic Lab inference:** Static unsupported-environment conditions belong in a custom Skip attribute; runtime service availability can use `Skip.Test` only when absence is an accepted environment condition. Product defects must fail, not skip. Developer tools or destructive local seeding helpers, if ever represented as tests, must be Explicit and separately categorized.

### 10.4 Names, categories, and properties

**Source fact:** `[DisplayName]` supports parameter substitution and can be extended through a formatter. `[Category]` and arbitrary `[Property(name,value)]` metadata appear in context, reports, and Testing Platform tree filters; properties can be inherited from base classes ([display names](https://tunit.dev/docs/extending/display-names), [custom properties](https://tunit.dev/docs/writing-tests/test-context#custom-properties)). Argument formatters control how complex parameter values appear in explorer names ([argument formatters](https://tunit.dev/docs/extending/argument-formatters)).

**Logic Lab inference:** Retain the repository's `{Subject}Tests` and `Method_Scenario_Outcome` method naming. Add a small, stable metadata vocabulary only when CI actually filters it, for example `Category=Unit`, `Category=Property`, `Category=Browser`, or a separate `Evidence=Differential`. Do not tag every fact redundantly or encode ownership only in free-form display names.

## 11. Context, output, artifacts, reports, and observability

### 11.1 TestContext

**Source fact:** `TestContext.Current` exposes metadata, categories/properties, result state after the body, state bags, dependencies, output, artifacts, and unique test-isolation names. `TestBuilderContext` is the discovery-time counterpart and can pass state forward to execution ([TestContext](https://tunit.dev/docs/writing-tests/test-context)). Console and logger output are captured per test; background work without the inherited async context can use `TestContext.MakeCurrent()`, while Activity baggage supports cross-boundary correlation when OpenTelemetry propagation is configured ([logging](https://tunit.dev/docs/extending/logging)).

**Logic Lab inference:** Use context isolation IDs for future database names, directories, ports, topics, and browser artifacts so default parallel execution cannot collide. State bags are diagnostic/lifecycle state, not a product data channel or an excuse for dependent tests.

### 11.2 Artifacts

**Source fact:** Tests attach files through `TestContext.Current.Output.AttachArtifact`; sessions attach shared files through `TestSessionContext.Current.AddArtifact`. Artifacts are forwarded through Microsoft Testing Platform to result files, CI systems, and IDE explorers. Official guidance recommends existence checks, descriptive metadata, per-test directories, and attaching heavy diagnostics mainly on failure ([artifacts](https://tunit.dev/docs/writing-tests/artifacts)).

**Logic Lab inference:** Future Playwright screenshots/traces/videos, imported project reproducers, compilation dumps, and failure traces should be artifacts rather than base64 console blobs. Never attach secrets, user project contents, or unbounded traces. Keep unit-test output quiet unless a failure needs evidence.

### 11.3 Built-in reports and extensions

**Source fact:** The `TUnit` meta-package includes Microsoft Testing Platform code coverage, TRX, and telemetry extensions. Coverlet packages are incompatible because they target VSTest. Optional Microsoft extensions provide crash and hang dumps ([extensions](https://tunit.dev/docs/extending/built-in-extensions), [coverage](https://tunit.dev/docs/extending/code-coverage)).

**Source fact:** TUnit generates a self-contained HTML report and machine-readable JSON sidecar by default in `TestResults`; reports include test output, exceptions, timing, properties, artifacts, and Activity timelines. Multi-project reports can be aggregated ([HTML report](https://tunit.dev/docs/guides/html-report), [report aggregation](https://tunit.dev/docs/guides/report-aggregation)). GitHub Actions gets an automatic job summary; TRX/JUnit integrations serve other CI platforms ([CI reporting](https://tunit.dev/docs/execution/ci-cd-reporting)).

**Source fact:** TUnit emits `System.Diagnostics.Activity` spans for test and runner lifecycle. The optional `TUnit.OpenTelemetry` package wires an OTLP-capable provider; the HTML reporter can render Activity timelines without requiring an external exporter ([OpenTelemetry](https://tunit.dev/docs/examples/opentelemetry)).

**Logic Lab inference:** The initial migration should enable no new external telemetry destination. Preserve normal console output, retain default local HTML/JSON reports only if their generated directory is already ignored, and make CI explicitly publish the chosen TRX/coverage/HTML artifacts. Add crash/hang dumps or OTel export only for a diagnosed need and apply repository data-handling limits.

## 12. CLI, filters, IDE, and CI contract

### 12.1 Filters

**Source fact:** TUnit uses Microsoft Testing Platform tree-node filters, not VSTest `--filter`. A VSTest-style `dotnet test --filter` can print help and run zero tests. The path is `/Assembly/Namespace/Class/Test`, with property predicates and boolean operators; `--treenode-filter` is the supported option ([filters](https://tunit.dev/docs/execution/test-filters), [Microsoft graph filter](https://github.com/microsoft/testfx/blob/main/docs/mstest-runner-graphqueryfiltering/graph-query-filtering.md)).

**Logic Lab inference:** Replace any future CI `--filter` examples with `--treenode-filter`, quote wildcard expressions, and use `--minimum-expected-tests` or an equivalent inventory check in filtered jobs to prevent a typo from silently producing a green zero-test run ([command flags](https://tunit.dev/docs/reference/command-line-flags)).

### 12.2 IDE support

**Source fact:** Visual Studio requires Testing Platform server mode, Rider requires Testing Platform support, and VS Code requires C# Dev Kit with the Testing Platform protocol enabled. TUnit can stream IDE output in real time via `TUNIT_ENABLE_IDE_STREAMING`, but official documentation leaves it disabled by default because of known Testing Platform IDE crash compatibility issues ([IDE setup](https://tunit.dev/docs/getting-started/running-your-tests#ide-support), [environment variables](https://tunit.dev/docs/reference/environment-variables#tunit_enable_ide_streaming)).

**Logic Lab inference:** Document the three IDE switches, but do not enable IDE streaming in repository defaults. It is a local opt-in diagnostic until the upstream compatibility warning is removed.

### 12.3 CI commands

**Logic Lab inference:** The migrated documentation should preserve a small command contract:

```text
dotnet build logic-lab.slnx --nologo
dotnet test --solution logic-lab.slnx
dotnet format logic-lab.slnx --verify-no-changes
git diff --check
```

Add report/coverage flags only in CI jobs that publish those outputs, and pass every TUnit/MTP option directly under the repository's .NET 10 native MTP mode—never after an extra `--`. Use `--maximum-parallel-tests` or `TUNIT_MAX_PARALLEL_TESTS` for agent capacity, `--timeout` for the whole run, and a minimum expected test count for filtered jobs. TUnit's official CI examples cover separate restore/build/test, TRX, coverage, Native AOT, caching, and constrained parallelism, but examples containing the extra separator are for the older compatibility path and must be normalized for this repository ([CI pipeline](https://tunit.dev/docs/examples/tunit-ci-pipeline), [.NET 10 MTP syntax](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)).

## 13. Proposed mapping for the existing tests

| Current pattern | Target pattern | Reason |
|---|---|---|
| `[Fact] public void` | `[Test] public async Task` when it asserts | awaited assertions execute and analyzer-enforced |
| `[Theory]` + each `[InlineData]` | `[Test]` + each `[Arguments]` | preserves every explicit row and expected value |
| many xUnit asserts on one returned contract | awaited chains plus a bounded `Assert.Multiple()` scope | report independent schema mismatches together |
| `Assert.IsType<T>` returning T | awaited `IsTypeOf<T>()` result | compile-time typed continuation |
| `Assert.Throws<T>` guards | fluent `ThrowsExactly<T>()`, with parameter name where contractual | richer guard evidence; static helper remains acceptable for simple cases |
| ordered enumerable equality | one awaited ordered sequence equality assertion | preserves order and avoids per-item async assertions |
| private width/value nested loops | `[MatrixDataSource]` cases plus scalar oracle | individual names, parallel cases, better failure localization |
| `Prop.ForAll(...).QuickCheckThrowOnFailure()` inside Fact | `[Test, FsCheckProperty]` parameterized property | native TUnit lifecycle, seed/shrink/replay, cancellation |
| `xunit.runner.json` parallel cap | TUnit discovery settings or CI `TUNIT_MAX_PARALLEL_TESTS` | TUnit owns scheduling; keep pure tests parallel |
| future xUnit collection fixture | keyed `ClassDataSource<T>` plus matching constraint/limiter if mutable | explicit resource lifetime and concurrency |
| future bUnit test in `.cs` | separate TUnit project in source-generation mode | bUnit documents that C# tests work with TUnit |
| bUnit test in `.razor` | unsupported with the selected TUnit setup; rewrite as `.cs` | Razor and TUnit generators cannot consume each other's output |

**Logic Lab inference:** The initial migration must preserve all current explicit truth-table rows and deterministic word-boundary cases. Matrix refactoring may increase the discovered invocation count; that is acceptable only when each added case corresponds to already executed loop coverage or a clearly documented new boundary. Do not claim equivalence from test count alone.

## 14. Verification and acceptance criteria for implementation

The later code migration should not be considered complete until all of the following hold:

1. Locked restore resolves centrally managed, qualified TUnit packages with no xUnit, `Microsoft.NET.Test.Sdk`, Coverlet, or stale runner-config asset.
2. The default source-generated mode builds with warnings as errors and no unawaited assertion diagnostics.
3. `--list-tests` shows every current explicit semantic row, plus intentional Matrix expansions, with stable readable names.
4. All nine current property-bearing test methods run through `TUnit.FsCheck`, still shrink failures, emit a replay seed, and preserve current generators/oracles.
5. Whole-solution `dotnet test` succeeds on the pinned .NET 10 SDK and returns a nonzero exit code for an intentionally failed test in a temporary verification experiment.
6. A temporary filtered run proves the repository's tree filter and minimum-test guard; no VSTest `--filter` remains in authoritative documentation.
7. Pure tests demonstrate safe parallel execution; any non-parallel or limited tests name the concrete shared resource being protected.
8. Timeout cancellation is passed into asynchronous test I/O; retry is absent from deterministic tests.
9. TRX, coverage, and HTML artifacts are produced only in their intended job/directory and do not dirty the worktree or expose sensitive content.
10. `dotnet format ... --verify-no-changes` and `git diff --check` pass, package lock files are reviewed, and the pre-migration xUnit package/configuration is fully removed.

## 15. Important traps and upstream ambiguities

- **Unawaited assertions can pass silently.** The analyzer is a guard, not permission to ignore review ([awaiting](https://tunit.dev/docs/assertions/awaiting)).
- **TUnit tree filters are not VSTest filters.** A familiar `--filter` can run zero tests ([filters](https://tunit.dev/docs/execution/test-filters)).
- **All tests are parallel candidates and every invocation gets a new class instance.** Mutable instance state is not a fixture; mutable shared data needs explicit ownership and scheduling ([things to know](https://tunit.dev/docs/writing-tests/things-to-know)).
- **The parallelism overview and current source differ in precision.** The page says the thread pool determines how many run, while current scheduler source imposes a default `ProcessorCount * 4` global cap. Treat the documented configuration APIs as stable and set an explicit CI cap instead of depending on either wording ([parallelism](https://tunit.dev/docs/execution/parallelism), [scheduler source](https://github.com/thomhurst/TUnit/blob/d0c74ca21e191e02a8fe56a878d4922046ba7b19/src/TUnit.Engine/Scheduling/TestScheduler.cs#L508-L570)).
- **Discovery is an execution phase.** Instance/async data that is not initialized yet can yield no tests; expensive discovery runs repeatedly in IDEs and CI ([property injection](https://tunit.dev/docs/writing-tests/property-injection#discovery-phase-initialization)).
- **Matrix and CombinedDataSources multiply cases.** Preserve bounds and make the intended count reviewable ([combined source performance](https://tunit.dev/docs/writing-tests/combined-data-source#performance-considerations)).
- **`.And` and `.Or` cannot be mixed in one chain.** Split the assertion or express one boolean predicate ([combining](https://tunit.dev/docs/assertions/combining-assertions#or-conditions)).
- **Retry is not stability.** It is appropriate only for a classified transient failure; deterministic failures must remain immediate ([retry](https://tunit.dev/docs/execution/retrying)).
- **Timeout is cooperative.** The linked token must reach async operations; a blocked unmanaged or synchronous call can outlive the logical failure ([timeouts](https://tunit.dev/docs/execution/timeouts)).
- **Native AOT is not automatic merely because source generation is active.** It requires publishing, and FsCheck/bUnit/dynamic patterns create explicit reflection boundaries ([engine modes](https://tunit.dev/docs/execution/engine-modes), [FsCheck limits](https://tunit.dev/docs/examples/fscheck#limitations)).
- **Coverlet and `Microsoft.NET.Test.Sdk` do not belong in this model.** Use the Microsoft Testing Platform extensions transitively supplied by TUnit ([installation](https://tunit.dev/docs/getting-started/installation)).
- **IDE streaming is experimental in practice.** The official environment-variable page warns of runner crashes and leaves it disabled ([environment variables](https://tunit.dev/docs/reference/environment-variables#tunit_enable_ide_streaming)).
- **Current TUnit documentation evolves quickly.** Pin a version, preserve lock files, and verify commands and analyzers against that exact package rather than assuming `main` documentation describes every older release.

## 16. Primary official sources

### TUnit overview, packages, and migration

- [`llms.txt` documentation index](https://tunit.dev/llms.txt)
- [TUnit introduction](https://tunit.dev/docs/intro)
- [Installation](https://tunit.dev/docs/getting-started/installation)
- [Running tests and IDE support](https://tunit.dev/docs/getting-started/running-your-tests)
- [xUnit migration guide and code fixer](https://tunit.dev/docs/migration/xunit)
- [TUnit 1.63.0 NuGet package](https://www.nuget.org/packages/TUnit/1.63.0)
- [TUnit.FsCheck 1.63.0 NuGet package](https://www.nuget.org/packages/TUnit.FsCheck/1.63.0)
- [NuGet live TUnit version index](https://api.nuget.org/v3-flatcontainer/tunit/index.json)
- [NuGet live TUnit.FsCheck version index](https://api.nuget.org/v3-flatcontainer/tunit.fscheck/index.json)
- [Official repository at inspected commit](https://github.com/thomhurst/TUnit/tree/d0c74ca21e191e02a8fe56a878d4922046ba7b19)
- [Microsoft Testing Platform overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro)

### Assertions

- [Awaiting assertions](https://tunit.dev/docs/assertions/awaiting)
- [Assertion basics](https://tunit.dev/docs/assertions/getting-started)
- [Combining assertions and scopes](https://tunit.dev/docs/assertions/combining-assertions)
- [Exception assertions](https://tunit.dev/docs/assertions/exceptions)
- [Task and async assertions](https://tunit.dev/docs/assertions/tasks-and-async)
- [Compile-time type checking](https://tunit.dev/docs/assertions/type-checking)
- [Source-generated custom assertions](https://tunit.dev/docs/assertions/extensibility/source-generator-assertions)

### Data and generation

- [Choosing a data approach](https://tunit.dev/docs/writing-tests/data-driven-overview)
- [Arguments](https://tunit.dev/docs/writing-tests/arguments)
- [Method data sources](https://tunit.dev/docs/writing-tests/method-data-source)
- [Class data sources](https://tunit.dev/docs/writing-tests/class-data-source)
- [Matrix tests](https://tunit.dev/docs/writing-tests/matrix-tests)
- [Combined data sources](https://tunit.dev/docs/writing-tests/combined-data-source)
- [Test data rows](https://tunit.dev/docs/writing-tests/test-data-row)
- [Data-source generators](https://tunit.dev/docs/extending/data-source-generators)
- [Deferred enumeration](https://tunit.dev/docs/writing-tests/defer-enumeration)
- [Dynamic tests](https://tunit.dev/docs/extending/dynamic-tests)
- [AOT and generic tests](https://tunit.dev/docs/writing-tests/aot)
- [TUnit.FsCheck](https://tunit.dev/docs/examples/fscheck)

### Scheduling and lifecycle

- [Parallelism](https://tunit.dev/docs/execution/parallelism)
- [Ordering and dependencies](https://tunit.dev/docs/writing-tests/ordering)
- [Hooks](https://tunit.dev/docs/writing-tests/hooks)
- [Complete lifecycle](https://tunit.dev/docs/writing-tests/lifecycle)
- [Property injection](https://tunit.dev/docs/writing-tests/property-injection)
- [Dependency injection](https://tunit.dev/docs/writing-tests/dependency-injection)
- [Event receivers](https://tunit.dev/docs/writing-tests/event-subscribing)
- [Timeouts](https://tunit.dev/docs/execution/timeouts)
- [Retries](https://tunit.dev/docs/execution/retrying)
- [Repeats](https://tunit.dev/docs/execution/repeating)

### Metadata, execution, and evidence

- [Engine modes](https://tunit.dev/docs/execution/engine-modes)
- [Filters](https://tunit.dev/docs/execution/test-filters)
- [Command flags](https://tunit.dev/docs/reference/command-line-flags)
- [Programmatic configuration](https://tunit.dev/docs/reference/programmatic-configuration)
- [Test context](https://tunit.dev/docs/writing-tests/test-context)
- [Artifacts](https://tunit.dev/docs/writing-tests/artifacts)
- [Built-in Testing Platform extensions](https://tunit.dev/docs/extending/built-in-extensions)
- [Code coverage](https://tunit.dev/docs/extending/code-coverage)
- [HTML reports](https://tunit.dev/docs/guides/html-report)
- [CI reporting](https://tunit.dev/docs/execution/ci-cd-reporting)
- [OpenTelemetry](https://tunit.dev/docs/examples/opentelemetry)
