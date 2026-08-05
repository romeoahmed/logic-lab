# Engine Performance Benchmarks

This BenchmarkDotNet project owns comparative CPU and managed-allocation evidence for
`LogicLab.Engine`. It does not define latency targets or replace browser traces, load tests,
runtime counters, or deployment profiling.

## Corpus

| Suite | Production scenario | Comparison and ratio baseline | Parameter sets | Methods | BDN cases | Corpus |
|---|---|---|---:|---:|---:|---|
| `VectorLogicBenchmarks` | packed AND, OR, and XOR | packed kernel versus the matching scalar operation; scalar baseline per operation | 3 widths | 6 | 18 | `vector-logic-v2` |
| `VectorConservativeMergeBenchmarks` | conservative vector merge | packed kernel versus scalar merge; scalar baseline | 3 width/value-count pairs | 2 | 6 | `vector-merge-v1` |
| `VectorNetResolutionBenchmarks` | multi-driver net resolution | scalar oracle and production driver projection versus the packed kernel; packed baseline | 3 width/driver-count pairs | 3 | 9 | `vector-net-v2` |
| `CompilerBenchmarks` | compile and lower an authored acyclic AND chain | the same public Compiler operation across scale; no ratio baseline | 3 gate counts | 1 | 3 | `and-chain-v1` |
| `SimulationSessionBenchmarks` | open a compiled Session and perform initial settlement | the same public Simulation operation across scale; no ratio baseline | 3 gate counts | 1 | 3 | `and-chain-v1` |

The suite contains 39 benchmark cases. The vector widths are 1, 130, and 1024 bits; 130
deliberately exercises a partial packed word. The merge and resolution counts are 1, 4, and
16. The module gate counts are 1, 32, and 256.

The binary-logic inputs repeat every 16 bits and cover every ordered pair of the four logic
states. Merge and resolution use deterministic state cycles. Circuit authoring, compilation
for Session input, vector construction, and scalar transposition run in `GlobalSetup`, outside
the measured operations. All benchmark methods return their result.

## Run

Restore the committed dependency graph and build the optimized host:

```sh
dotnet restore logic-lab.slnx --locked-mode --nologo
dotnet build logic-lab.slnx -c Release --no-restore --nologo
```

List or validate cases before measuring them:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --list flat
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --job Dry --filter '*' --noOverwrite
```

Use a short, filtered run while iterating:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --job Short --filter '*VectorLogicBenchmarks*' --noOverwrite
```

Use the default job for final evidence, still splitting suites when useful:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --filter '*VectorNetResolutionBenchmarks*' --noOverwrite
```

`BenchmarkSwitcher` requires a `--filter` for non-interactive execution. `Dry` only proves
generated-project compilation and one execution of each case. `Short` is directional
development evidence. `--noOverwrite` keeps runs distinct under `BenchmarkDotNet.Artifacts/`.
Inspect the generated `*-report-github.md` summary rather than treating console iteration logs
as the result.

## Interpretation

Ratios are meaningful only inside the declared logical group and parameter set. Absolute means
are specific to the runtime, build, operating system, hardware, power state, and surrounding
load. Record those facts with the corpus revision when retaining a decision. Managed allocation
is reported without the redundant per-generation collection columns.

The [performance review](../../docs/research/performance-review.md) owns the source record,
historical measurements, and decision ledger. Benchmark artifacts stay untracked.

## Evidence gaps

The present corpus covers packed combinational kernels, flat acyclic compilation, and initial
Session settlement. The representative corpus is not frozen until implementation-plan item 34.
Cyclic settlement, hierarchy, sequential execution, memory, scheduled batches, invalid
compilation, cancellation, and policy-limit paths remain explicit benchmark gaps rather than
speculative fixtures.

Domain authoring needs operation-sequence evidence. Application/Web concurrency, queueing, and
capacity need load tests and runtime counters. Blazor rendering and browser interaction need
browser traces. Those concerns should not be modeled as BenchmarkDotNet microbenchmarks.

The implementation follows BenchmarkDotNet's official guidance for
[optimized command-line runs](https://benchmarkdotnet.org/articles/guides/good-practices.html),
[parameterization](https://benchmarkdotnet.org/articles/features/parameterization.html),
[category-scoped baselines](https://benchmarkdotnet.org/articles/samples/IntroCategoryBaseline.html),
[console arguments](https://benchmarkdotnet.org/articles/guides/console-args.html), and
[managed-allocation diagnostics](https://benchmarkdotnet.org/articles/configs/diagnosers.html).
