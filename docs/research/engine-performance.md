# Engine Performance Evidence

> Measured 2026-08-05; benchmark corpus revalidated 2026-08-30 (Asia/Shanghai)
> Scope: Engine implementation and BenchmarkDotNet evidence
> Authority: research and measured decisions; [Architecture](../architecture.md) and [Engineering](../engineering.md) own normative rules

## 1. Conclusion

The implemented project has no evidence for a cross-cutting cache, pool, unsafe path, runtime
switch, or new public performance interface. Its packed four-state kernels are strongly
supported at production widths. Two LINQ-backed projections in Simulation settlement were
confirmed as local allocation hot spots and replaced with explicit, exact-sized array fills.
No Module contract changed.

The first checkpoint covered only multi-driver vector resolution. The 2026-08-05
checkpoint added packed logic, conservative merge, Compilation, and initial Session
settlement. The 2026-08-30 corpus redesign added deterministic combinational,
hierarchical, feedback, sequential, memory, advance, snapshot-read, and Trace-read
workloads. The [benchmark README](../../benchmarks/LogicLab.Engine.Benchmarks/README.md)
owns the live case matrix; this dated record no longer copies counts that change with
the corpus. A Dry run validates construction and execution, not performance. Retained
measurements still require an explicit Release job in the target environment.

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

## 3. Audit method

At the dated review checkpoint, the audit followed repository ownership from Domain through
Engine, Application, Presentation, and Web, plus all tests and build infrastructure. It combined:

1. locked restore, warning-clean build, tests, formatting, and whitespace gates;
2. an additional non-incremental `AnalysisLevel=10-all` build to expose optional analyzer
   findings without changing the repository's normative analyzer level;
3. structural searches for sync-over-async, accidental `async void`, repeated enumeration,
   allocation-heavy strings and slices, regex construction, JSON reflection, unbounded
   collections, static mutable dependencies, and avoidable hot-path LINQ;
4. interface review against Architecture and the Simulation/Compiler contracts; and
5. baseline/candidate BenchmarkDotNet comparisons on the same runtime, hardware, corpus, and
   job.

The expanded analyzer pass found no production CA18xx performance diagnostic. Targeted
inspection likewise found no synchronous wait, per-call regex, ad hoc `HttpClient`, reflection
JSON path, unsafe block, pooling lifetime, or static mutable cache requiring correction. This
negative evidence does not replace a runtime profile, but it prevents speculative rewrites.

## 4. Measured decisions

| Candidate                                                     | Evidence                                                                                                                    | Decision               |
| ------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ---------------------- |
| indexed fill for net-driver projection                        | production shape improved 57.876 to 24.765 ns, 72.414 to 39.133 ns, and 298.687 to 232.653 ns; allocation fell in all cases | retain                 |
| indexed fill for combinational gate inputs                    | 256-gate public Session-open path improved 145.554 to 136.105 us; allocation fell 56 KB, or 224 B per gate                  | retain                 |
| ordinal-based `ReadOnlySpan<int>` resolver interface          | removed a temporary array but slowed the 1024-bit production shape to 348.600 ns                                            | remove                 |
| pooling, stack allocation, unsafe access, runtime/GC switches | no deployment profile or end-to-end evidence; inspected arrays escape as result-owned values or are small exact scratch     | reject for this review |

The retained changes preserve the existing `IReadOnlyList<LogicVector>` kernel contracts and
one allocation for required reference storage. They remove iterator and collection-expression
construction overhead without adding overload families, ownership rules, or special-case
branches. This record retains the measured decisions; the
[benchmark README](../../benchmarks/LogicLab.Engine.Benchmarks/README.md) owns the current
corpus definition and run commands.

## 5. Benchmark coverage and gaps

| Area                                  | Current executable corpus                                                                                  | Next honest evidence                                                                       |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| packed Boolean operations             | scalar differential BDN cases at 1, 130, and 1024 bits                                                     | rerun on supported deployment architectures                                                |
| conservative merge and net resolution | scalar differential BDN cases across width and fan-in/driver count                                         | adversarial density cases after corpus freeze                                              |
| Compilation                           | public Compiler operation over 11 deterministic flat, hierarchical, feedback, sequential, and memory cases | invalid and policy-limit cases only when their decision value justifies permanent run cost |
| initial Simulation settlement         | open, settle, and close over the same 11 circuit cases                                                     | target-environment default-job evidence after the representative corpus freezes            |
| scheduled and clocked Simulation work | 9 open/optional-schedule/advance/close cases over flat, D flip-flop, and RAM circuits                      | Hot Swap, cancellation, and policy-limit workflows after their corpora are versioned       |
| Session reads                         | snapshots over 3 probe topologies and Trace windows over 16/256/4096 retained transitions                  | additional Trace density and retention shapes after delivery item 34                       |
| Domain authoring                      | authoring is setup infrastructure, not a measured operation                                                | versioned edit sequences, not isolated helper microbenchmarks                              |
| Application/Web capacity              | no deployment profile                                                                                      | load tests plus queue, ThreadPool, allocation, GC, and latency telemetry                   |
| Blazor/browser rendering              | no browser corpus                                                                                          | browser performance traces and interaction measurements                                    |

The split follows Architecture: comparative kernels belong to BenchmarkDotNet, while browser
and load behavior require browser traces and load tests. Microsoft likewise treats counters,
traces, dumps, and profiling as complementary diagnostic tools
([.NET diagnostics overview](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)).

## 6. Corpus revalidation checkpoint

The 2026-08-30 rewrite gives benchmark infrastructure a deep interface: benchmark methods name
only the public operation being measured, `EngineBenchmarkCorpus` owns request and Session
orchestration, and the circuit catalog hides Project Editor intents and Compilation source-map
lookups. Complex inputs use stable `ParamsSource` values. Construction and Trace population run
in `GlobalSetup`; read fixtures close in `GlobalCleanup`; mutable workflows own a fresh Session
per invocation and close it in the measured workflow. Concrete success-result casts make an
unexpected rejection fail the case.

A Release build and all 70 Dry cases completed successfully. The 11 Compiler and 26 Simulation
cases also completed under `ShortRun`; those results were used only to confirm that shape and
scale axes produce useful directional separation. Dry and Short timings are intentionally not
retained as performance decisions.

One representative default Release job retained the following Trace-read checkpoint:

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

## 7. Measurement record

The linked BenchmarkDotNet and Microsoft Learn sources were checked on 2026-08-05 and again on
2026-08-30. Section 4 preserves the first local comparison; section 6 records the expanded
corpus. Neither checkpoint is a release threshold or a promise for other hardware.
