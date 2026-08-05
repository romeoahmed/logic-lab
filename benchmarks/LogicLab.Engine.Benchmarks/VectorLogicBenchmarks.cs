using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using LogicLab.Domain;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class VectorLogicBenchmarks
{
    private LogicVector left = null!;
    private LogicValue[] leftValues = null!;
    private LogicVector right = null!;
    private LogicValue[] rightValues = null!;

    [Params(1, 130, 1024)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        leftValues = [.. Enumerable.Range(0, Width).Select(LeftValue)];
        rightValues = [.. Enumerable.Range(0, Width).Select(RightValue)];
        left = new LogicVector(leftValues);
        right = new LogicVector(rightValues);
    }

    [BenchmarkCategory("And")]
    [Benchmark(Baseline = true)]
    public LogicVector ScalarAnd()
    {
        var values = new LogicValue[Width];
        for (var bitIndex = 0; bitIndex < values.Length; bitIndex++)
        {
            values[bitIndex] = ScalarLogic.And(
                leftValues[bitIndex],
                rightValues[bitIndex]);
        }

        return new LogicVector(values);
    }

    [BenchmarkCategory("And")]
    [Benchmark]
    public LogicVector PackedAnd() => VectorLogic.And(left, right);

    [BenchmarkCategory("Or")]
    [Benchmark(Baseline = true)]
    public LogicVector ScalarOr()
    {
        var values = new LogicValue[Width];
        for (var bitIndex = 0; bitIndex < values.Length; bitIndex++)
        {
            values[bitIndex] = ScalarLogic.Or(
                leftValues[bitIndex],
                rightValues[bitIndex]);
        }

        return new LogicVector(values);
    }

    [BenchmarkCategory("Or")]
    [Benchmark]
    public LogicVector PackedOr() => VectorLogic.Or(left, right);

    [BenchmarkCategory("Xor")]
    [Benchmark(Baseline = true)]
    public LogicVector ScalarXor()
    {
        var values = new LogicValue[Width];
        for (var bitIndex = 0; bitIndex < values.Length; bitIndex++)
        {
            values[bitIndex] = ScalarLogic.Xor(
                leftValues[bitIndex],
                rightValues[bitIndex]);
        }

        return new LogicVector(values);
    }

    [BenchmarkCategory("Xor")]
    [Benchmark]
    public LogicVector PackedXor() => VectorLogic.Xor(left, right);

    private static LogicValue LeftValue(int bitIndex) =>
        (LogicValue)((bitIndex + 1) & 3);

    private static LogicValue RightValue(int bitIndex) =>
        (LogicValue)(((bitIndex / 4) + 2) & 3);
}
