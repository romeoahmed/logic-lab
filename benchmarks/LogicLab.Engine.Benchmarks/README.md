# Vector Net Resolution Benchmark

This BenchmarkDotNet project measures the production settlement kernel that resolves one
multi-driver Logic Vector. The comparison axis is the per-bit scalar semantic oracle versus
the packed production resolver; `ScalarOracle` is the baseline. Corpus revision
`vector-net-v1` has three intentional cases: one-bit/single-driver, a 130-bit word-tail case
with four drivers, and a 1024-bit case with sixteen drivers. Input construction and scalar
transposition happen in `GlobalSetup`, outside measurement.

Validate generated benchmark code with:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release -- --filter '*' --job Dry
```

Record a comparative iteration with:

```sh
dotnet run --project benchmarks/LogicLab.Engine.Benchmarks -c Release -- --filter '*' --job Short
```

Benchmark artifacts report throughput distribution and managed allocation. They are evidence
for deciding whether a candidate is worth retaining, not a universal latency or capacity gate.

## Latest local comparison

The 2026-08-02 `ShortRun` on .NET 10.0.10, macOS arm64, Apple M5 produced this directional
comparison (three measured iterations per case):

| Case | Scalar mean ± SD | Packed mean ± SD | Packed/scalar | Packed allocation |
|---|---:|---:|---:|---:|
| `w1-d1` | 0.989 ± 0.017 ns | 21.128 ± 0.679 ns | 21.36 | 248 B |
| `w130-d4` | 507.109 ± 9.396 ns | 32.827 ± 0.288 ns | 0.06 | 328 B |
| `w1024-d16` | 11,178.437 ± 197.350 ns | 210.505 ± 1.430 ns | 0.02 | 848 B |

The packed path pays an output-allocation cost for the one-bit case, but is about 15× faster
at `w130-d4` and 53× faster at `w1024-d16`. This supports retaining the existing packed
resolver; it does not justify another optimization or establish a release threshold. Re-run
the declared job on target deployment hardware before calibrating one.
