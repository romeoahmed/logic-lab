using BenchmarkDotNet.Attributes;
using LogicLab.Domain;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("kernel")]
public class VectorNetResolutionBenchmarks
{
    private int[] driverOrdinals = null!;
    private LogicVector[] vectorDrivers = null!;
    private LogicValue[][] scalarDriversByBit = null!;

    [ParamsSource(nameof(Cases))]
    public ResolutionCase Case { get; set; }

    public static IEnumerable<ResolutionCase> Cases =>
    [
        new(Width: 1, DriverCount: 1),
        new(Width: 130, DriverCount: 4),
        new(Width: 1024, DriverCount: 16),
    ];

    [GlobalSetup]
    public void Setup()
    {
        var valuesByDriver = Enumerable.Range(0, Case.DriverCount)
            .Select(driverIndex => Enumerable.Range(0, Case.Width)
                .Select(bitIndex => Value(bitIndex, driverIndex))
                .ToArray())
            .ToArray();
        vectorDrivers = [.. valuesByDriver.Select(values => new LogicVector(values))];
        driverOrdinals = [.. Enumerable.Range(0, Case.DriverCount)];
        scalarDriversByBit = [.. Enumerable.Range(0, Case.Width)
            .Select(bitIndex => valuesByDriver
                .Select(values => values[bitIndex])
                .ToArray())];
    }

    [Benchmark]
    public LogicVector ScalarOracle()
    {
        var values = new LogicValue[Case.Width];
        for (var bitIndex = 0; bitIndex < values.Length; bitIndex++)
        {
            values[bitIndex] = NetResolver.Resolve(
                scalarDriversByBit[bitIndex]).Value;
        }

        return new LogicVector(values);
    }

    [Benchmark(Baseline = true)]
    public LogicVector PackedKernel()
    {
        return VectorNetResolver.Resolve(Case.Width, vectorDrivers).Value;
    }

    [Benchmark]
    public LogicVector ProductionCallShape()
    {
        var drivers = new LogicVector[driverOrdinals.Length];
        for (var index = 0; index < drivers.Length; index++)
        {
            drivers[index] = vectorDrivers[driverOrdinals[index]];
        }

        return VectorNetResolver.Resolve(
            Case.Width,
            drivers).Value;
    }

    private static LogicValue Value(int bitIndex, int driverIndex)
    {
        return (LogicValue)((bitIndex * 17 + driverIndex * 13 + 3) & 3);
    }

    public readonly record struct ResolutionCase(int Width, int DriverCount)
    {
        public override string ToString() => $"w{Width}-d{DriverCount}";
    }
}
