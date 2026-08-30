# Engine Performance Benchmarks

This BenchmarkDotNet project owns comparative CPU and managed-allocation evidence for
`LogicLab.Engine`. It does not define latency targets or replace browser traces, load tests,
runtime counters, or deployment profiling.

## Corpus

| Suite | Production scenario | Comparison axis | Ratio baseline | Parameters | Methods | BDN cases | Corpus revision |
|---|---|---|---|---:|---:|---:|---|
| `VectorLogicBenchmarks` | packed AND, OR, and XOR | packed kernel versus the matching scalar operation | scalar, independently for each operation | 3 widths | 6 | 18 | `vector-logic-v2` |
| `VectorConservativeMergeBenchmarks` | conservative vector merge | packed kernel versus the scalar oracle | scalar oracle | 3 width/value-count pairs | 2 | 6 | `vector-merge-v1` |
| `VectorNetResolutionBenchmarks` | multi-driver net resolution | scalar oracle and production driver projection versus the packed kernel | packed kernel | 3 width/driver-count pairs | 3 | 9 | `vector-net-v2` |
| `CompilerBenchmarks` | compile and lower authored combinational, hierarchical, feedback, sequential, and memory circuits | the same public Compiler operation across shape and scale | none; scale series | 11 circuit cases | 1 | 11 | `engine-circuits-v2` |
| `SimulationOpenBenchmarks` | open, initially settle, and close a compiled Session | the same public Simulation workflow across shape and scale | none; scale series | 11 circuit cases | 1 | 11 | `engine-circuits-v2` |
| `SimulationSnapshotReadBenchmarks` | read an open Session snapshot | the same read across probe topology and scale | none; scale series | 3 circuit cases | 1 | 3 | `engine-circuits-v2` |
| `SimulationAdvanceBenchmarks` | open a Session, optionally schedule input, advance one quiescent boundary, and close | the same end-to-end command workflow across combinational, sequential, and memory circuits | none; scale series | 9 circuit cases | 1 | 9 | `engine-circuits-v2` |
| `SimulationTraceReadBenchmarks` | materialize a retained alternating-input Trace window | the same read across retained-transition counts | none; scale series | 3 transition counts | 1 | 3 | `alternating-trace-v1` |

The suite contains 16 methods and 70 benchmark cases. The vector widths are 1, 130, and
1024 bits; 130 deliberately exercises a partial packed word. The merge and resolution counts
are 1, 4, and 16.

`engine-circuits-v2` uses stable case names and deterministic authored inputs:

- flat two-input AND chains at 1, 32, and 256 gates;
- hierarchical chains at 32 and 256 instances of a user-authored inverter definition;
- 32 independent self-inverting feedback nets;
- shared-data, shared-clock D flip-flop banks at 1, 32, and 256 registers; and
- 16 x 8, 256 x 8, and 4096 x 8 single-port RAMs initialized with deterministic words.

Compilation and Session requests are constructed in `GlobalSetup`, outside the measured
operation. Read-only Session fixtures are opened once in `GlobalSetup` and closed in
`GlobalCleanup`. Mutable command workflows create and close a Session inside each invocation so
iterations neither share mutated state nor leak Sessions. Trace history is populated in setup.
The suite deliberately avoids `IterationSetup`: BenchmarkDotNet forces invocation and unroll
counts to one when iteration setup or cleanup is present, which distorts small microbenchmarks.
Every benchmark returns a concrete successful outcome; a rejected or failed outcome aborts the
case instead of becoming a misleading timing sample.

## Run

Run from a terminal without an attached debugger. Restore the committed dependency graph and
build the optimized host:

```sh
dotnet restore logic-lab.slnx --locked-mode --nologo
dotnet build logic-lab.slnx -c Release --no-restore --nologo
```

List or validate all cases before measuring them:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --list flat
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --job Dry --filter '*' --noOverwrite
```

Use a short, filtered run while iterating:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --job Short --filter '*SimulationAdvanceBenchmarks*' --noOverwrite
```

Use the default job for retained evidence, still filtering or splitting suites when useful:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --filter '*SimulationTraceReadBenchmarks*' --noOverwrite
```

`BenchmarkSwitcher` requires a `--filter` for non-interactive execution. `Dry` proves only that
BenchmarkDotNet can generate, compile, and execute every case once. `Short` is directional
development evidence. The default job is the retained measurement unless a review records a
different explicit job. `--noOverwrite` keeps runs distinct under
`BenchmarkDotNet.Artifacts/`. Inspect the generated `*-report-github.md` summary; console
iteration lines are not the result.

## Interpretation

Ratios are meaningful only inside the declared logical group and parameter set. Scale-only
module suites intentionally have no baseline: a ratio between different circuit shapes would
imply equivalence the corpus does not claim. Absolute means are specific to the runtime, build,
operating system, hardware, power state, and surrounding load. Record those facts with the
corpus revision when retaining a decision. `MemoryDiagnoser` reports managed allocation without
the redundant per-generation collection columns.

The [performance evidence](../../docs/research/engine-performance.md) owns the source record,
historical measurements, and decision ledger. Benchmark artifacts stay untracked.

## Evidence gaps

The representative corpus is not frozen until implementation-plan item 34. Invalid
compilation, cooperative cancellation, policy-limit rejection, Hot Swap, broader memory shapes,
and versioned Domain edit sequences remain explicit gaps. Add a permanent case only when it has
a production scenario, a useful comparison axis, and enough expected decision value to justify
its run cost.

Application/Web concurrency, queueing, and capacity need load tests and runtime counters.
Blazor rendering and browser interaction need browser traces. Those concerns should not be
modeled as BenchmarkDotNet microbenchmarks.

The implementation follows BenchmarkDotNet's official guidance for
[optimized command-line runs](https://benchmarkdotnet.org/articles/guides/good-practices.html),
[parameterization](https://benchmarkdotnet.org/articles/features/parameterization.html),
[setup and cleanup](https://benchmarkdotnet.org/articles/features/setup-and-cleanup.html),
[category-scoped baselines](https://benchmarkdotnet.org/articles/features/baselines.html),
[console arguments](https://benchmarkdotnet.org/articles/guides/console-args.html), and
[managed-allocation diagnostics](https://benchmarkdotnet.org/articles/configs/diagnosers.html).
