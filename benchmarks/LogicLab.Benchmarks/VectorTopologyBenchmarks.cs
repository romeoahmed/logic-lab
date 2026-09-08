using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using LogicLab.Domain;
using LogicLab.Engine;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("kernel")]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class VectorTopologyBenchmarks
{
    private LogicVector[] inputs = null!;
    private int outputWidth;

    [Params(1, 65, 1024)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        inputs = [Vector(Width), Vector(Width + 1), Vector(Width + 3)];
        outputWidth = Width * 2 + 7;
        if (!ScalarConcat().ContentEquals(PackedConcat())
            || !ScalarZeroExtend().ContentEquals(PackedZeroExtend())
            || !ScalarSignExtend().ContentEquals(PackedSignExtend()))
        {
            throw new InvalidOperationException("Packed topology differs from the scalar baseline.");
        }
    }

    [BenchmarkCategory("Concat")]
    [Benchmark(Baseline = true)]
    public LogicVector ScalarConcat()
    {
        var values = new LogicValue[inputs.Sum(input => input.Width)];
        var offset = 0;
        foreach (var input in inputs)
        {
            for (var index = 0; index < input.Width; index++)
            {
                values[offset + index] = ScalarLogic.NormalizeInput(input[index]);
            }

            offset += input.Width;
        }

        return new LogicVector(values);
    }

    [BenchmarkCategory("Concat")]
    [Benchmark]
    public LogicVector PackedConcat() => VectorLogic.Concat(inputs);

    [BenchmarkCategory("ZeroExtend")]
    [Benchmark(Baseline = true)]
    public LogicVector ScalarZeroExtend() => ScalarExtend(signExtend: false);

    [BenchmarkCategory("ZeroExtend")]
    [Benchmark]
    public LogicVector PackedZeroExtend() => VectorLogic.ZeroExtend(inputs[0], outputWidth);

    [BenchmarkCategory("SignExtend")]
    [Benchmark(Baseline = true)]
    public LogicVector ScalarSignExtend() => ScalarExtend(signExtend: true);

    [BenchmarkCategory("SignExtend")]
    [Benchmark]
    public LogicVector PackedSignExtend() => VectorLogic.SignExtend(inputs[0], outputWidth);

    private LogicVector ScalarExtend(bool signExtend)
    {
        var input = inputs[0];
        var values = new LogicValue[outputWidth];
        for (var index = 0; index < input.Width; index++)
        {
            values[index] = ScalarLogic.NormalizeInput(input[index]);
        }

        var fill = signExtend ? values[input.Width - 1] : LogicValue.Zero;
        Array.Fill(values, fill, input.Width, outputWidth - input.Width);
        return new LogicVector(values);
    }

    private static LogicVector Vector(int width) =>
        new([.. Enumerable.Range(0, width).Select(index => (LogicValue)((index + 3) & 3))]);
}
