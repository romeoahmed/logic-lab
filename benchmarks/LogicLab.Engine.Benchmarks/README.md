# Engine Performance Benchmarks

This BenchmarkDotNet project owns comparative CPU and managed-allocation evidence for
`LogicLab.Engine`. It does not define a latency target or replace browser traces, load tests,
or deployment profiling.

## Corpus

| Benchmark | Measured operation | Cases | Comparison axis |
|---|---|---|---|
| `VectorLogicBenchmarks` | packed AND, OR, and XOR | widths 1, 130, and 1024 | scalar semantic oracle versus packed kernel |
| `VectorConservativeMergeBenchmarks` | conservative vector merge | `w1-v1`, `w130-v4`, `w1024-v16` | scalar semantic oracle versus packed kernel |
| `VectorNetResolutionBenchmarks` | multi-driver net resolution | `w1-d1`, `w130-d4`, `w1024-d16` | scalar oracle, packed kernel, and production driver projection |
| `CompilerBenchmarks` | compile and lower an authored acyclic AND chain | 1, 32, and 256 gates | scale comparison |
| `SimulationSessionBenchmarks` | open a compiled Session and perform initial settlement | 1, 32, and 256 gates | scale comparison |

The current corpus revisions are `vector-logic-v1`, `vector-merge-v1`, `vector-net-v1`, and
`and-chain-v1`. The 130-bit cases deliberately exercise a partial packed word. Deterministic
input and Project construction happens in `GlobalSetup`, outside measurement. Kernel suites
retain their scalar implementations as semantic baselines. Module suites deliberately do not
invent a faster baseline; their parameter rows compare scale within the same public operation.

## Run

Restore the committed graph, then build and validate all generated benchmark executables:

```sh
dotnet restore logic-lab.slnx --locked-mode --nologo
dotnet build logic-lab.slnx -c Release --no-restore --nologo
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --job Dry --filter '*'
```

Record a short local comparison:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release --no-build -- --job Short --filter '*'
```

Use one `--filter` followed by multiple patterns when selecting more than one suite. A full
default BenchmarkDotNet job is required before establishing a durable threshold; `Dry` proves
execution only, and `Short` is directional development evidence.

## Latest local evidence

These `ShortRun` results were recorded on 2026-08-05 with BenchmarkDotNet 0.15.8, .NET SDK
10.0.302, .NET 10.0.10 Arm64 RyuJIT, macOS arm64, and an Apple M5. Means are not portable to a
different runtime, machine, power state, or deployment profile.

### Measured call-site changes

Replacing the LINQ-backed driver projection in `SimulationRuntime.ResolveNet` with one exact
array allocation and an indexed fill reduced both time and allocation in every measured
production call shape:

| Case | Before | After | Before allocation | After allocation |
|---|---:|---:|---:|---:|
| `w1-d1` | 57.876 ns | 24.765 ns | 504 B | 280 B |
| `w130-d4` | 72.414 ns | 39.133 ns | 608 B | 384 B |
| `w1024-d16` | 298.687 ns | 232.653 ns | 1,464 B | 1,000 B |

The same local replacement for gate-input projection was measured through the complete public
Session-open path. The 256-gate result improved by about 6.5%, while allocation fell by exactly
224 B per gate. The smaller-case timing differences are within ShortRun noise, but their
allocation reduction is deterministic.

| Gates | Before | After | Before allocation | After allocation |
|---:|---:|---:|---:|---:|
| 1 | 2.202 us | 2.194 us | 11.53 KB | 11.31 KB |
| 32 | 18.322 us | 18.681 us | 103.83 KB | 96.83 KB |
| 256 | 145.554 us | 136.105 us | 775.73 KB | 719.73 KB |

An alternative ordinal-based `ReadOnlySpan<int>` resolver API removed the temporary driver
array but slowed the `w1024-d16` production shape from 298.687 ns to 348.600 ns. It was removed
instead of enlarging the internal interface for an allocation-only win.

### Kernel comparisons

| Width | Scalar AND/OR/XOR range | Packed range | Scalar allocation | Packed allocation |
|---:|---:|---:|---:|---:|
| 1 | 15.26-15.43 ns | 10.16-10.26 ns | 168 B | 104 B |
| 130 | 497.05-507.15 ns | 14.44-15.48 ns | 456 B | 136 B |
| 1024 | 3,740.07-3,958.50 ns | 36.89-38.53 ns | 2,440 B | 344 B |

| Merge case | Scalar | Packed | Scalar allocation | Packed allocation |
|---|---:|---:|---:|---:|
| `w1-v1` | 14.94 ns | 10.20 ns | 168 B | 104 B |
| `w130-v4` | 549.77 ns | 20.81 ns | 456 B | 136 B |
| `w1024-v16` | 9,477.45 ns | 198.71 ns | 2,440 B | 344 B |

| Net case | Scalar oracle | Packed kernel | Production shape | Production allocation |
|---|---:|---:|---:|---:|
| `w1-d1` | 0.937 ns | 21.641 ns | 24.765 ns | 280 B |
| `w130-d4` | 485.838 ns | 32.925 ns | 39.133 ns | 384 B |
| `w1024-d16` | 10,712.822 ns | 210.953 ns | 232.653 ns | 1,000 B |

The one-bit net scalar oracle remains faster; the packed representation earns its complexity
at wider production sizes and is not claimed as a universal replacement.

### Module scale baselines

| Gates | Compile mean | Compile allocation | Open + settle mean | Open + settle allocation |
|---:|---:|---:|---:|---:|
| 1 | 6.043 us | 26.59 KB | 2.194 us | 11.31 KB |
| 32 | 69.528 us | 275.33 KB | 18.681 us | 96.83 KB |
| 256 | 577.682 us | 2,054.93 KB | 136.105 us | 719.73 KB |

## Evidence gaps

The present suite covers the packed combinational kernels, flat acyclic compilation, and
initial Session settlement. The representative corpus is not frozen until implementation-plan
item 34. Cyclic settlement, hierarchy, sequential execution, memory, scheduled batches,
invalid compilation, cancellation, and policy-limit paths therefore remain explicit benchmark
gaps rather than speculative fixtures.

Domain authoring latency needs operation-sequence evidence. Application/Web concurrency,
queueing, and capacity need load tests and runtime counters. Blazor rendering and browser
interaction need browser traces. Those concerns should not be modeled as BenchmarkDotNet
microbenchmarks.

See [Performance Review](../../docs/research/performance-review.md) for the official-source
record, project-wide audit, and decision ledger. BenchmarkDotNet's own guidance explains its
[measurement model](https://benchmarkdotnet.org/articles/guides/how-it-works.html),
[good practices](https://benchmarkdotnet.org/articles/guides/good-practices.html), and
[diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html).
