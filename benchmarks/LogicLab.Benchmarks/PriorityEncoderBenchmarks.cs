using BenchmarkDotNet.Attributes;
using LogicLab.Domain;
using LogicLab.Engine;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("kernel")]
public class PriorityEncoderBenchmarks
{
    private LogicValue[] inputs = null!;

    [Params(4, 64, 256)]
    public int InputCount { get; set; }

    [Params(false, true)]
    public bool Uncertain { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        inputs = new LogicValue[InputCount];
        if (Uncertain)
        {
            Array.Fill(inputs, LogicValue.X);
        }
        else
        {
            inputs[^1] = LogicValue.One;
        }

        var result = Encode();
        var expected = Uncertain ? LogicValue.X : LogicValue.One;
        if (result.Valid != expected
            || Enumerable.Range(0, result.Index.Width).Any(bit => result.Index[bit] != expected))
        {
            throw new InvalidOperationException("The priority encoder did not match the benchmark input.");
        }
    }

    [Benchmark]
    public (LogicVector Index, LogicValue Valid) Encode()
    {
        var result = CombinationalEvaluation.PriorityEncoder(inputs, lowestIndex: true);
        return (result.Index, result.Valid);
    }
}
