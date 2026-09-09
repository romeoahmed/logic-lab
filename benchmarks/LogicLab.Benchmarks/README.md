# Module Benchmarks

This BenchmarkDotNet project owns comparative CPU and managed-allocation evidence for
Domain, Engine, Presentation, and Project Format. It does not define latency targets or replace browser traces, load
tests, runtime counters, or deployment profiling.

## Corpus

| Suite                               | Scenario                                                              | Comparison                                                               |
| ----------------------------------- | --------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| `ProjectEditorMoveBenchmarks`       | one or all components in small and large definitions                  | public atomic edit cost across selection size                            |
| `ProjectPackageReadBenchmarks`      | complete import of small and large component collections              | public read cost, including spool, ZIP, validation, and canonicalization |
| `SchematicProjectionBenchmarks`     | small and large definitions containing multiline annotations          | public static projection cost with fixed matched text metrics            |
| `CoreLibraryLookupBenchmarks`       | first, middle, last, and missing Contract IDs                         | linear oracle against the production immutable index                     |
| `VectorLogicBenchmarks`             | packed AND, OR, XOR                                                   | packed kernels against matching scalar operations                        |
| `VectorTopologyBenchmarks`          | concatenation and zero/sign extension at aligned and unaligned widths | packed kernels against normalized scalar projection                      |
| `VectorConservativeMergeBenchmarks` | four-state vector merge                                               | packed kernel against scalar oracle                                      |
| `VectorNetResolutionBenchmarks`     | multi-driver resolution                                               | scalar and projected-driver paths against packed resolution              |
| `PriorityEncoderBenchmarks`         | known and all-unknown inputs at three input counts                    | one encoder operation across candidate-set sizes                         |
| `CompilerBenchmarks`                | flat, hierarchical, feedback, sequential, and memory circuits         | one public Compiler operation across shape and scale                     |
| `CompilerAdmissionBenchmarks`       | invalid Port, cancellation, and policy rejection on three circuit shapes | closed rejection-path cost, with the declared outcome verified         |
| `SimulationOpenBenchmarks`          | open and settle                                                       | one public Session workflow across shape and scale                       |
| `SimulationSnapshotReadBenchmarks`  | read an open Session                                                  | read cost across probe topology and scale                                |
| `SimulationAdvanceBenchmarks`       | schedule and advance                                                  | end-to-end command workflow across circuit families                      |
| `SimulationTraceReadBenchmarks`     | transition and summary windows                                        | Trace reads across retained history                                      |
| `SimulationHotSwapBenchmarks`       | compatible state migration and rejected state loss, including 65-bit RAM | additional workflow cost relative to opening and closing the same Session |

The versioned corpus and parameter values live with benchmark source. It includes
word-tail widths, small-to-large scale series, hierarchy, feedback, registers, and
memory. Compilation requests are prepared outside measured operations. Read-only
Sessions are shared only where state cannot change; mutable workflows create and
close a Session per invocation. Every benchmark must return a successful public
outcome or fail the case.

## Run

Build Release first with the repository command, then inspect or dry-run the selected
cases:

```sh
dotnet run --project benchmarks/LogicLab.Benchmarks -c Release --no-build -- --list flat
dotnet run --project benchmarks/LogicLab.Benchmarks -c Release --no-build -- --job Dry --filter '*' --noOverwrite
```

Use `Short` only for directional iteration. Retained evidence uses the default job
unless its record names another job explicitly:

```sh
dotnet run --project benchmarks/LogicLab.Benchmarks -c Release --no-build -- --job Short --filter '*SimulationAdvanceBenchmarks*' --noOverwrite
dotnet run --project benchmarks/LogicLab.Benchmarks -c Release --no-build -- --filter '*SimulationTraceReadBenchmarks*' --noOverwrite
```

Non-interactive `BenchmarkSwitcher` runs require `--filter`. A failed benchmark,
critical validation error, or empty execution returns nonzero; listing and help remain
successful inspection commands. `Dry` proves only that a case can generate, compile,
and execute once. Inspect the generated report rather than console iteration lines.
Benchmark artifacts remain untracked.

## Interpret results

Ratios are meaningful only inside the declared logical group and parameter set.
Scale-only module suites intentionally have no baseline because different shapes are
not equivalent operations. Absolute means depend on runtime, build, operating system,
hardware, power state, and load; retain those facts with the corpus revision.

The [performance evidence](../../docs/research/module-performance.md) owns historical
measurements and decisions. Admission and Hot Swap cases use versioned benchmark
categories; RAM includes both 8-bit words and a 65-bit packed-word boundary. The
Hot Swap baseline includes Open and Close, so its ratio measures added migration or
rejection work rather than isolated kernel latency. Versioned authoring sequences
are semantic test evidence, not a substitute for Workspace history/load measurements.
[Delivery item 34](../../docs/delivery.md#completed) owns freezing the
representative qualification set.

Application/Web concurrency and database capacity need load tests and runtime
counters. Blazor rendering and interaction need browser traces. Do not turn either
concern into a BenchmarkDotNet microbenchmark.

Benchmark practice follows the official guidance for
[optimized runs](https://benchmarkdotnet.org/articles/guides/good-practices.html),
[parameters](https://benchmarkdotnet.org/articles/features/parameterization.html),
[setup and cleanup](https://benchmarkdotnet.org/articles/features/setup-and-cleanup.html),
[baselines](https://benchmarkdotnet.org/articles/features/baselines.html), and
[diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html).
