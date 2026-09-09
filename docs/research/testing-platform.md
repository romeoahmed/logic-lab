# .NET Testing Platform Evidence

> Sources reviewed: 2026-09-09
> Authority: rationale for [Engineering](../engineering.md#verification), not a second test policy

## One executable test stack

.NET 10's native MTP driver requires every selected test project to use MTP;
VSTest projects cannot join the same run. It forwards extension options to the test
applications and provides a minimum-test check for filtered runs. This supports the
single runner selected in `global.json`. Commands and filter syntax belong in
[AGENTS.md](../../AGENTS.md), and package versions belong in
[Directory.Packages.props](../../Directory.Packages.props).
[Microsoft CLI reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)

A test application that executes zero tests exits with code 8. A class filter over
the whole solution can therefore report the selected tests as passing while the
other applications fail the invocation. Focused runs select the owning project;
the full suite keeps the zero-test guard.
[MTP exit codes](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-troubleshooting),
[TUnit filters](https://tunit.dev/docs/execution/test-filters/)

TUnit runs as an ordinary executable project and supplies the entry point, common
usings, coverage, and TRX extensions. Its installation guide explicitly excludes
`Microsoft.NET.Test.Sdk` and Coverlet. Logic Lab therefore needs no parallel runner
or reporting stack. [TUnit installation](https://tunit.dev/docs/getting-started/installation/)

## Assertions and generated properties

Fluent TUnit assertions execute when awaited; omitting `await` can leave a passing
test that checked nothing. The analyzer protects this boundary, while typed equality
and collection assertions retain the actual mismatch in the failure report.
[TUnit assertions](https://tunit.dev/docs/assertions/getting-started/)

`TUnit.FsCheck` supports ordinary asynchronous assertion tests and FsCheck `Property`
results with labels, classification, custom generators, shrinking, and replay seeds.
A property therefore does not need an async wrapper when it returns `Property`.
FsCheck still requires reflection and dynamic code generation: source-generated
TUnit discovery does not make this integration Native AOT compatible. Logic Lab
keeps its semantic properties in the JIT suite.
[TUnit FsCheck integration](https://tunit.dev/docs/examples/fscheck/)

## Concurrency and Web boundaries

TUnit makes tests eligible for parallel execution by default. Keyed exclusions
coordinate specific shared resources; a limiter bounds concurrent use of a pool.
These controls support the repository's isolation-first policy without serializing
unrelated evidence. [TUnit parallelism](https://tunit.dev/docs/execution/parallelism/)

bUnit's TUnit guidance identifies a source-generator constraint: TUnit and Razor
cannot consume one another's generated output. Its supported combination uses
C# test files under `Microsoft.NET.Sdk`. The page's sample package versions and
VSTest-era coverage configuration are not Logic Lab's package policy; the TUnit
installation guide and the pinned repository graph govern that choice.
[bUnit project setup](https://bunit.dev/docs/getting-started/create-test-project.html)

bUnit also exposes `WaitForStateAsync(Func<bool>)`, which observes the initial state
and subsequent renders with the same timeout contract. Asynchronous diagnostic and
Canvas lifecycle tests await this overload so pending interop does not block a test
thread. The predicate remains synchronous; the wait does not. This changes neither
the asserted behavior nor the timeout and does not retry a failed test.
[bUnit waiting API](https://bunit.dev/api/Bunit.RenderedComponentWaitForHelperExtensions.html)

The shared component fixture sets bUnit’s bounded completion wait to five seconds.
Whole-solution execution shares dispatcher CPU with concurrent browser and module
tests; the library’s one-second default can expire before a predicate is scheduled.
This is a scheduling allowance, not a retry or a renderer performance threshold.

When a test also depends on background application work, await evidence at that
application seam before starting the render wait. The finite-Run fixture observes
the actual paused Workspace projection, with test cancellation, before asserting
the paused UI; bUnit's render timeout does not budget the Simulation scheduler.

bUnit can exercise a component's rendered markup and callbacks without proving real
browser layout, Canvas, or transport behavior. Logic Lab assigns those checks to
Playwright against the actual host; host-only seams use `TUnit.AspNetCore`.
[Engineering](../engineering.md#verification) owns the evidence required at each seam.

The component evidence gate uses MTP's `--report-trx` extension and a fresh
`--results-directory`. The report's test definitions and results identify the exact
assembly, class, method, display name, and outcome. TRX does not contain a verified
source digest, so the repository workflow owns the same-checkout build/run binding.
[MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports)

CI and Release call the same local reusable workflow. Its successful output fixes
the release checkout to the tested commit; a moved tag fails the release's tag check.
[GitHub reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows)

Artifact uploads explicitly admit completed failed runs with `!cancelled()` and the
test step's `outcome`; the default `success()` condition would hide their evidence.
Verification still returns its original failure status and blocks Release.
[GitHub status checks](https://docs.github.com/en/actions/reference/workflows-and-actions/expressions#status-check-functions),
[step outcomes](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#steps-context)
