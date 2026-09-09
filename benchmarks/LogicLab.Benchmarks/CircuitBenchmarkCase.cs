namespace LogicLab.Benchmarks;

public enum CircuitBenchmarkShape
{
    FlatAndChain,
    HierarchicalInverterChain,
    InverterFeedbackBank,
    DFlipFlopBank,
    SinglePortRam,
}

public readonly record struct CircuitBenchmarkCase
{
    public CircuitBenchmarkCase(CircuitBenchmarkShape shape, int size, uint wordWidth = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfZero(wordWidth);
        Shape = shape;
        Size = size;
        WordWidth = wordWidth;
    }

    public CircuitBenchmarkShape Shape { get; }

    public int Size { get; }

    public uint WordWidth { get; }

    public override string ToString() => Shape switch
    {
        CircuitBenchmarkShape.FlatAndChain => $"flat-and-v2-g{Size}",
        CircuitBenchmarkShape.HierarchicalInverterChain => $"hier-not-v1-i{Size}",
        CircuitBenchmarkShape.InverterFeedbackBank => $"feedback-not-v1-r{Size}",
        CircuitBenchmarkShape.DFlipFlopBank => $"dff-bank-v1-r{Size}",
        CircuitBenchmarkShape.SinglePortRam => $"ram-v1-d{Size}-w{WordWidth}",
        _ => throw new InvalidOperationException("Unknown benchmark circuit shape."),
    };
}
