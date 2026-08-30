using BenchmarkDotNet.Attributes;
using LogicLab.Domain;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("kernel")]
public class VectorConservativeMergeBenchmarks
{
    private LogicValue[][] scalarValuesByBit = null!;
    private LogicVector[] vectors = null!;

    [ParamsSource(nameof(Cases))]
    public MergeCase Case { get; set; }

    public static IEnumerable<MergeCase> Cases =>
    [
        new(Width: 1, ValueCount: 1),
        new(Width: 130, ValueCount: 4),
        new(Width: 1024, ValueCount: 16),
    ];

    [GlobalSetup]
    public void Setup()
    {
        var valuesByVector = Enumerable.Range(0, Case.ValueCount)
            .Select(valueIndex => Enumerable.Range(0, Case.Width)
                .Select(bitIndex => Value(bitIndex, valueIndex))
                .ToArray())
            .ToArray();
        vectors = [.. valuesByVector.Select(values => new LogicVector(values))];
        scalarValuesByBit = [.. Enumerable.Range(0, Case.Width)
            .Select(bitIndex => valuesByVector
                .Select(values => values[bitIndex])
                .ToArray())];
    }

    [Benchmark(Baseline = true)]
    public LogicVector ScalarOracle()
    {
        var values = new LogicValue[Case.Width];
        for (var bitIndex = 0; bitIndex < values.Length; bitIndex++)
        {
            values[bitIndex] = ConservativeMerge.Merge(scalarValuesByBit[bitIndex]);
        }

        return new LogicVector(values);
    }

    [Benchmark]
    public LogicVector PackedKernel()
    {
        return VectorConservativeMerge.Merge(vectors);
    }

    private static LogicValue Value(int bitIndex, int valueIndex)
    {
        return (LogicValue)((bitIndex * 17 + valueIndex * 13 + 3) & 3);
    }

    public readonly record struct MergeCase(int Width, int ValueCount)
    {
        public override string ToString() => $"w{Width}-v{ValueCount}";
    }
}
