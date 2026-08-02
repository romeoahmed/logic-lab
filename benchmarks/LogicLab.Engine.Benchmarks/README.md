# Vector Net Resolution Benchmark

This BenchmarkDotNet project measures the production settlement path that resolves one
multi-driver Logic Vector. The comparison axis is the per-bit scalar semantic oracle versus
both the packed resolver kernel and its production call shape; `ScalarOracle` is the baseline.
Corpus revision
`vector-net-v1` has three intentional cases: one-bit/single-driver, a 130-bit word-tail case
with four drivers, and a 1024-bit case with sixteen drivers. Input construction and scalar
transposition happen in `GlobalSetup`, outside measurement. `ProductionCallShape` keeps the
driver-ordinal projection and `ToArray()` used by `SimulationRuntime.ResolveNet` inside each
measured operation.

Validate generated benchmark code with:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release -- --filter '*' --job Dry --noOverwrite
```

Record a comparative iteration with:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release -- --filter '*' --job Short --noOverwrite
```

Benchmark artifacts report throughput distribution and managed allocation. They are evidence
for deciding whether a candidate is worth retaining, not a universal latency or capacity gate.

## Latest local comparison

The 2026-08-02 `ShortRun` on .NET 10.0.10, macOS arm64, Apple M5 produced this directional
comparison (three measured iterations per case):

| Case | Scalar mean ± SD | Kernel mean ± SD | Production shape mean ± SD | Production/scalar | Kernel allocation | Production allocation |
|---|---:|---:|---:|---:|---:|---:|
| `w1-d1` | 0.971 ± 0.061 ns | 20.585 ± 0.038 ns | 33.558 ± 0.808 ns | 34.65 | 248 B | 392 B |
| `w130-d4` | 471.687 ± 20.271 ns | 31.620 ± 0.067 ns | 47.779 ± 0.076 ns | 0.10 | 328 B | 496 B |
| `w1024-d16` | 11,455.581 ± 520.260 ns | 206.806 ± 1.651 ns | 242.808 ± 0.177 ns | 0.02 | 848 B | 1,112 B |

The production call shape exposes the allocation omitted by a kernel-only benchmark: it adds
144 B, 168 B, and 264 B respectively for driver projection. The complete path is slower for
the one-bit case, but remains about 10× faster at `w130-d4` and 47× faster at `w1024-d16` than
the scalar oracle. This supports retaining the existing packed resolver while making its
call-site overhead visible; it does not justify another optimization or establish a release
threshold. Re-run the declared job on target deployment hardware before calibrating one.
