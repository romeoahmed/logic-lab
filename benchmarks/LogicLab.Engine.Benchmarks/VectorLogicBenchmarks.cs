using BenchmarkDotNet.Attributes;
using LogicLab.Domain;

namespace LogicLab.Engine.Benchmarks;

public enum LogicOperation
{
    And,
    Or,
    Xor,
}

public readonly record struct LogicCase(LogicOperation Operation, int Width)
{
    public override string ToString()
    {
        var operation = Operation switch
        {
            LogicOperation.And => "and",
            LogicOperation.Or => "or",
            LogicOperation.Xor => "xor",
            _ => $"value-{(int)Operation}",
        };
        return $"{operation}-w{Width}";
    }
}

[MemoryDiagnoser]
[RankColumn]
public class VectorLogicBenchmarks
{
    private static readonly int[] Widths = [1, 130, 1024];

    private LogicVector left = null!;
    private LogicValue[] leftValues = null!;
    private LogicVector right = null!;
    private LogicValue[] rightValues = null!;

    [ParamsSource(nameof(ProductionCases))]
    public LogicCase Case { get; set; }

    public static IEnumerable<LogicCase> ProductionCases =>
        from operation in Enum.GetValues<LogicOperation>()
        from width in Widths
        select new LogicCase(operation, width);

    [GlobalSetup]
    public void Setup()
    {
        leftValues = Enumerable.Range(0, Case.Width)
            .Select(bitIndex => Value(bitIndex, salt: 3))
            .ToArray();
        rightValues = Enumerable.Range(0, Case.Width)
            .Select(bitIndex => Value(bitIndex, salt: 11))
            .ToArray();
        left = new LogicVector(leftValues);
        right = new LogicVector(rightValues);
    }

    [Benchmark(Baseline = true)]
    public LogicVector ScalarOracle()
    {
        var values = new LogicValue[Case.Width];
        for (var bitIndex = 0; bitIndex < values.Length; bitIndex++)
        {
            values[bitIndex] = Case.Operation switch
            {
                LogicOperation.And => ScalarLogic.And(
                    leftValues[bitIndex],
                    rightValues[bitIndex]),
                LogicOperation.Or => ScalarLogic.Or(
                    leftValues[bitIndex],
                    rightValues[bitIndex]),
                LogicOperation.Xor => ScalarLogic.Xor(
                    leftValues[bitIndex],
                    rightValues[bitIndex]),
                _ => throw new InvalidOperationException(
                    "The benchmark Logic operation is undefined."),
            };
        }

        return new LogicVector(values);
    }

    [Benchmark]
    public LogicVector PackedKernel()
    {
        return Case.Operation switch
        {
            LogicOperation.And => VectorLogic.And(left, right),
            LogicOperation.Or => VectorLogic.Or(left, right),
            LogicOperation.Xor => VectorLogic.Xor(left, right),
            _ => throw new InvalidOperationException(
                "The benchmark Logic operation is undefined."),
        };
    }

    private static LogicValue Value(int bitIndex, int salt)
    {
        return (LogicValue)((bitIndex * 17 + salt) & 3);
    }
}
