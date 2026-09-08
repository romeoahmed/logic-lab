# Module Performance Evidence

> Dated local measurements; no production capacity or latency guarantee.
> [Engineering](../engineering.md) owns optimization rules. The
> [benchmark README](../../benchmarks/LogicLab.Benchmarks/README.md) owns the live corpus and commands.

Keep a change when it improves a measured workload or removes a demonstrated
correctness inconsistency. Public outcomes, scalar oracles, ownership, and cancellation
remain part of the comparison. A Dry job checks execution only; short jobs guide
iteration. Browser interaction and Application capacity require traces and load tests.

BenchmarkDotNet isolates processes, warms up, consumes results, and measures repeated
operations ([measurement model](https://benchmarkdotnet.org/articles/guides/how-it-works.html)).
Managed allocation comes from `MemoryDiagnoser`
([diagnoser guidance](https://benchmarkdotnet.org/articles/configs/diagnosers.html)).
Compare matched inputs and record runtime, hardware, job, and uncertainty with each decision.

## Retained decisions

### Initial kernel and settlement comparisons, 2026-08-05

| Change                        | Observed result                                                                              | Decision |
| ----------------------------- | -------------------------------------------------------------------------------------------- | -------- |
| indexed net-driver projection | 57.876 → 24.765 ns, 72.414 → 39.133 ns, 298.687 → 232.653 ns; lower allocation in every case | retain   |
| indexed combinational inputs  | 256-gate Session open: 145.554 → 136.105 μs; 56 KB less allocation                           | retain   |
| ordinal-based span resolver   | 1024-bit case slowed to 348.600 ns despite one fewer array                                   | remove   |

These comparisons support local array fills, not a general cache, pool, unsafe path,
or new public performance interface. Recheck them when the workload or runtime changes.

### Compiler consolidation, 2026-09-05

The flat and hierarchical Compiler paths now share occurrence expansion and lowering.
This fixes two path-dependent facts: adding an unreferenced definition could change
elaborated work, and a flat Net could list the same receiver twice. Regression tests
cover both. The entry occurrence now counts once in every Compilation; the Compiler
semantic version identifies the revised translation and evidence.

A same-machine Release `ShortRun` comparison (one launch, three warmups and three
measurements) used the existing 256-gate and 256-instance corpus cases. Baseline and
candidate both included the preceding Domain and vector-allocation cleanup.

| Case               | Separate paths | Unified path | Managed allocation, before → after |
| ------------------ | -------------: | -----------: | ---------------------------------: |
| `flat-and-v2-g256` |       598.7 μs |     665.1 μs |                    1.96 → 2.15 MiB |
| `hier-not-v1-i256` |     1,488.3 μs |   1,414.9 μs |                    3.86 → 3.68 MiB |

Environment: Apple M5, macOS 26.6.2, SDK 10.0.400, .NET 10.0.11 Arm64, BenchmarkDotNet
0.15.8, concurrent workstation GC. Candidate standard deviations were 3.61 μs and
29.16 μs respectively. These short measurements are directional evidence, not release
capacity or latency claims. The consolidation retains a measured cost for the flat
case in exchange for one consistent translation implementation; it is not presented
as a universal speedup. It reuses the resolved-instance index and sorts scoped Nets
once before grouping, without adding a cache or changing a public interface.

### Library Snapshot lookup, 2026-09-08

The fixed built-in catalog now resolves through one Library Snapshot. Its private
`FrozenDictionary` index replaces a capturing linear predicate; the ordered schema
still determines enumeration and digests. Microsoft recommends this collection for
trusted keys built once and read repeatedly ([FrozenDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2?view=net-10.0)).

`CoreLibraryLookupBenchmarks` compares the previous scan with the public lookup,
using distinct string instances to represent deserialized IDs. A Release `ShortRun`
on Apple M5, macOS 26.6.2, SDK 10.0.400, .NET 10.0.11 Arm64 and BenchmarkDotNet 0.15.8
used one launch, three warmups, and three measurements:

| Contract position | Linear scan | Indexed lookup | Allocation per lookup |
| ----------------- | ----------: | -------------: | --------------------: |
| first             |     5.19 ns |        4.52 ns |            32 B → 0 B |
| middle            |    19.17 ns |        4.62 ns |            32 B → 0 B |
| last              |    24.93 ns |        6.76 ns |            32 B → 0 B |
| missing           |    23.17 ns |        1.92 ns |            32 B → 0 B |

These are local directional measurements, not application throughput or capacity
claims. The first-entry timing difference is small; the allocation removal and
middle/last lookup reduction justify the index. It is built only from internal
schema recipes, never from imported keys. Run the comparison with
`--job Short --filter '*CoreLibraryLookupBenchmarks*'` through the benchmark project.

### Batch component movement, 2026-09-08

`ProjectEditorMoveBenchmarks` measures the public immutable edit operation for
16 or 1024 unconnected components, moving the last component or all components
in reverse order. Setup and intent construction are outside the measurement.
The baseline searched the definition for every requested ID and then rebuilt a
second replacement index. The retained implementation indexes requests once,
checks and replaces components in one pass, and preserves untouched instances.

| Components | Moved | Baseline mean | Retained mean | Baseline allocation | Retained allocation |
| ---------- | ----: | ------------: | ------------: | ------------------: | ------------------: |
| 16         |     1 |        875 ns |        731 ns |             2.37 KB |             1.77 KB |
| 16         |   all |       4.04 μs |       2.00 μs |             7.84 KB |             4.91 KB |
| 1024       |     1 |      28.85 μs |      24.48 μs |            18.12 KB |            17.52 KB |
| 1024       |   all |      1.634 ms |      0.190 ms |           416.90 KB |           225.75 KB |

Both runs used the same Apple M5/macOS 26.6.2 environment, SDK 10.0.400,
.NET 10.0.11 Arm64, BenchmarkDotNet 0.15.8, one launch, three warmups and three
measurements. The retained 1024-component all-move case had a 6.64 μs standard
deviation. These short runs support eliminating repeated scans; they do not
establish deployment latency. Reproduce with `--job Short --filter
'*ProjectEditorMoveBenchmarks*'` through the benchmark project.

### Packed topology, 2026-09-08

`VectorTopologyBenchmarks` compares concatenation and zero/sign extension against
the normalized scalar projection and repacking previously used by these kernels.
Concatenation uses three inputs of widths `w`, `w + 1`, and `w + 3`; extension
produces `2w + 7` bits. Input patterns include all four Logic Values. Every setup
compares full results before measuring, and Engine tests check scalar equivalence,
all 64 concatenation offsets, all four sign values, input ownership, and zero tail
padding.

| Operation   | Width | Scalar mean | Packed mean | Scalar allocation | Packed allocation |
| ----------- | ----: | ----------: | ----------: | ----------------: | ----------------: |
| Concat      |     1 |    29.35 ns |    17.19 ns |             136 B |             104 B |
| Concat      |    65 |   518.69 ns |    22.27 ns |             376 B |             152 B |
| Concat      |  1024 |  7820.69 ns |    83.34 ns |            3976 B |             872 B |
| Zero extend |     1 |    18.62 ns |     9.34 ns |             144 B |             104 B |
| Zero extend |    65 |   224.60 ns |    12.14 ns |             304 B |             136 B |
| Zero extend |  1024 |  3091.68 ns |    44.25 ns |            2696 B |             616 B |
| Sign extend |     1 |    20.43 ns |    11.08 ns |             144 B |             104 B |
| Sign extend |    65 |   289.45 ns |    13.50 ns |             304 B |             136 B |
| Sign extend |  1024 |  4275.76 ns |    48.27 ns |            2696 B |             616 B |

The environment and ShortRun settings match the batch-movement record above.
These local comparisons support retaining direct bit-plane operations without an
intermediate `LogicValue[]`; they do not estimate total Simulation speedup. Run
`--job Short --filter '*VectorTopologyBenchmarks*'` to reproduce the corpus.

### Streaming package schema validation, 2026-09-08

The import path previously built a `JsonDocument` solely to check member shapes,
then parsed the bytes again into generated DTOs. It now walks the original UTF-8
with `Utf8JsonReader`, using the same generated metadata and a reader value copy
to find out-of-order discriminators. This removes the intermediate tree and
per-member capturing predicates while preserving format diagnostics. Reader value
copies support independent lookahead ([Microsoft guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to#an-alternative-way-to-do-polymorphic-deserialization)).

`ProjectPackageReadBenchmarks` measures the complete public import, including
temporary spooling, ZIP decoding, integrity checks, validation, Domain construction,
and canonicalization. Setup exports 16 or 1024 unconnected NOT components; every
measured read must return the expected content digest. The original implementation
was compiled separately from the candidate without changing the Domain baseline.

| Components | DOM mean | Streaming mean | DOM allocation | Streaming allocation |
| ---------- | -------: | -------------: | -------------: | -------------------: |
| 16         |   764 μs |         557 μs |      512.01 KB |            479.58 KB |
| 1024       |  8.76 ms |        8.32 ms |     9479.76 KB |           7684.69 KB |

Both Release ShortRuns used Apple M5, macOS 26.6.2, SDK 10.0.400, .NET 10.0.11
Arm64 and BenchmarkDotNet 0.15.8, with one launch, three warmups and three
measurements. The small case was noisy (standard deviations 305 and 95 μs), and
timing confidence intervals overlap in both cases. These runs support the allocation
reduction—about 1.75 MiB per 1024-component import—without establishing a latency
improvement. Reproduce the current path with `--job Short --filter
'*ProjectPackageReadBenchmarks*'`; the shared test corpus also covers all nested
member shapes, legal permutations, escaped names, and late discriminators.

## Trace-read checkpoint, 2026-08-30

The default Release job measured the public Trace query after setup populated the
Session. This is a scale checkpoint, not a before/after optimization comparison.

| Corpus                 | Retained transitions |        Mean | Managed allocation |
| ---------------------- | -------------------: | ----------: | -----------------: |
| `alternating-trace-v1` |                   16 |    378.1 ns |            1.03 KB |
| `alternating-trace-v1` |                  256 |  4,637.8 ns |            10.5 KB |
| `alternating-trace-v1` |                4,096 | 71,378.6 ns |          160.59 KB |

The job used BenchmarkDotNet 0.15.8, SDK 10.0.400, .NET 10.0.11 Arm64 RyuJIT with Concurrent
Workstation GC, macOS Tahoe 26.6.2, and Apple M5. The host could not raise process priority, so
the checkpoint is local comparative evidence rather than a deployment threshold. One preceding
full run reported a multimodal 16-transition distribution; an isolated rerun and the retained
full rerun were stable, while the pure query implementation and allocations were unchanged.
This run-to-run scheduling evidence is another reason not to promote local absolute means to a
product promise. The benchmark README owns the exact case matrix, commands, corpus revisions,
and interpretation rules.

## Remaining evidence

The benchmark source owns current shape and scale cases. Invalid/policy-limited
Compilation, Hot Swap, cancellation, broader memory layouts, and mixed edit sequences
need versioned inputs before their performance can support a decision. Deployment
architectures and representative workloads remain part of
[production qualification](../delivery.md#production-qualification).

Use browser traces for frame and interaction costs; use load tests and runtime
telemetry for queueing, ThreadPool, GC, and Application capacity. Counters, traces,
and profiles complement benchmarks ([.NET diagnostics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)).
