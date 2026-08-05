# .NET Performance Review

> Verified 2026-08-05 (Asia/Shanghai)
> Scope: project-wide .NET 10 implementation and BenchmarkDotNet evidence
> Authority: research and measured decisions; normative ownership remains in Architecture and the .NET Engineering Baseline

## 1. Conclusion

The implemented project has no evidence for a cross-cutting cache, pool, unsafe path, runtime
switch, or new public performance interface. Its packed four-state kernels are strongly
supported at production widths. Two LINQ-backed projections in Simulation settlement were
confirmed as local allocation hot spots and replaced with explicit, exact-sized array fills.
No Module contract changed.

Benchmark coverage previously proved only multi-driver vector resolution. It now also compares
the packed binary-logic and conservative-merge kernels and establishes scale baselines for
Compilation and initial Session settlement on a real authored circuit. The remaining gaps are
classified by the evidence capable of answering them instead of treating BenchmarkDotNet as a
universal performance test tool.

## 2. Source facts and project inferences

- **Source fact:** BenchmarkDotNet builds generated benchmark projects and runs benchmarks in
  isolated processes, selects invocation counts, performs warmup and measurement iterations,
  and subtracts overhead according to the selected job. Its output is statistical evidence,
  not a single stopwatch sample
  ([measurement model](https://benchmarkdotnet.org/articles/guides/how-it-works.html)).
- **Source fact:** the official guidance requires optimized builds, realistic inputs, work
  large enough to measure, result consumption, and attention to environment and variance
  ([good practices](https://benchmarkdotnet.org/articles/guides/good-practices.html)).
- **Source fact:** `MemoryDiagnoser` reports managed allocation and GC collections with low
  overhead, while other diagnosers have platform and runtime constraints
  ([diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html)).
- **Source fact:** `ReadOnlySpan<T>` is a stack-only contiguous read view, not immutable
  ownership. Adding a span overload changes an interface and does not itself prove better code
  generation ([API](https://learn.microsoft.com/en-us/dotnet/api/system.readonlyspan-1?view=net-10.0),
  [memory and spans](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)).
- **Source fact:** CA1851 identifies potentially repeated `IEnumerable<T>` enumeration, but
  analyzer output is a lead to inspect rather than proof of a hot path
  ([rule](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1851)).
- **Project inference:** scalar implementations remain semantic oracles; a packed or explicit
  implementation remains only when a production-shaped comparison demonstrates value. Public
  Module values stay owned and immutable, and storage layout stays internal.

BenchmarkDotNet 0.15.8 is the centrally locked version used for this review. It is the current
official release, and the 0.15 line includes .NET 10 support
([repository](https://github.com/dotnet/BenchmarkDotNet),
[releases](https://github.com/dotnet/BenchmarkDotNet/releases)).

## 3. Audit method

At the dated review checkpoint, the audit followed repository ownership from Domain through
Engine, Application, Presentation, and Web, plus all tests and build infrastructure. It combined:

1. locked restore, warning-clean build, 582-test baseline, formatting, and whitespace gates;
2. an additional non-incremental `AnalysisLevel=10-all` build to expose optional analyzer
   findings without changing the repository's normative analyzer level;
3. structural searches for sync-over-async, accidental `async void`, repeated enumeration,
   allocation-heavy strings and slices, regex construction, JSON reflection, unbounded
   collections, static mutable dependencies, and avoidable hot-path LINQ;
4. interface review against Architecture and the Simulation/Compiler contracts; and
5. baseline/candidate BenchmarkDotNet comparisons on the same runtime, hardware, corpus, and
   job.

The expanded analyzer pass produced 498 advisory warnings, predominantly test-only CA2007
guidance that conflicts with the repository's application/test conventions. It found no
production CA18xx performance diagnostic. Targeted inspection likewise found no synchronous
wait, per-call regex, ad hoc `HttpClient`, reflection JSON path, unsafe block, pooling lifetime,
or static mutable cache requiring correction. This negative evidence does not replace a runtime
profile, but it prevents speculative repository-wide rewrites.

## 4. Measured decisions

| Candidate | Evidence | Decision |
|---|---|---|
| indexed fill for net-driver projection | production shape improved 57.876 to 24.765 ns, 72.414 to 39.133 ns, and 298.687 to 232.653 ns; allocation fell in all cases | retain |
| indexed fill for combinational gate inputs | 256-gate public Session-open path improved 145.554 to 136.105 us; allocation fell 56 KB, or 224 B per gate | retain |
| ordinal-based `ReadOnlySpan<int>` resolver interface | removed a temporary array but slowed the 1024-bit production shape to 348.600 ns | remove |
| pooling, stack allocation, unsafe access, runtime/GC switches | no deployment profile or end-to-end evidence; current arrays escape as result-owned values or are small exact scratch | reject for this review |

The retained changes preserve the existing `IReadOnlyList<LogicVector>` kernel contracts and
one allocation for required reference storage. They remove iterator and collection-expression
construction overhead without adding overload families, ownership rules, or special-case
branches. This record retains the measured decisions; the
[benchmark README](../../benchmarks/LogicLab.Engine.Benchmarks/README.md) owns the current
corpus definition and run commands.

## 5. Benchmark coverage and gaps

| Area | Current evidence | Next honest evidence |
|---|---|---|
| packed Boolean operations | scalar differential BDN cases at 1, 130, and 1024 bits | rerun on supported deployment architectures |
| conservative merge and net resolution | scalar differential BDN cases across width and fan-in/driver count | adversarial density cases after corpus freeze |
| flat acyclic Compilation | public Compiler operation on 1/32/256-gate authored circuits | hierarchy and diagnostic corpora after implementation-plan item 34 |
| initial Simulation settlement | public Session open on the same circuit scale | cyclic, sequential, memory, and policy-limit corpora |
| scheduled Simulation work | no representative workload yet | BDN only after versioned Trigger Batch/Trace scenarios exist |
| Domain authoring | no sequence corpus | measured edit sequences, not isolated helper microbenchmarks |
| Application/Web capacity | no deployment profile | load tests plus queue, ThreadPool, allocation, GC, and latency telemetry |
| Blazor/browser rendering | no browser corpus | browser performance traces and interaction measurements |

The split follows Architecture: comparative kernels belong to BenchmarkDotNet, while browser
and load behavior require browser traces and load tests. Microsoft likewise treats counters,
traces, dumps, and profiling as complementary diagnostic tools
([.NET diagnostics overview](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)).

## 6. Source and access record

The linked BenchmarkDotNet repository, releases, guides, and Microsoft Learn .NET 10 pages were
accessed on 2026-08-05. Local verification used SDK 10.0.302, runtime 10.0.10, BenchmarkDotNet
0.15.8, and Apple M5 arm64. ShortRun numbers are directional local evidence; no result in this
note is a release threshold or a promise for other hardware.
