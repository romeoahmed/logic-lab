using BenchmarkDotNet.Attributes;
using LogicLab.Domain;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class VectorNetResolutionBenchmarks
{
    private int[] driverOrdinals = null!;
    private LogicVector[] vectorDrivers = null!;
    private LogicValue[][] scalarDriversByBit = null!;

    [ParamsSource(nameof(ProductionCases))]
    public ResolutionCase Case { get; set; }

    public static IEnumerable<ResolutionCase> ProductionCases =>
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
        vectorDrivers = valuesByDriver
            .Select(values => new LogicVector(values))
            .ToArray();
        driverOrdinals = Enumerable.Range(0, Case.DriverCount).ToArray();
        scalarDriversByBit = Enumerable.Range(0, Case.Width)
            .Select(bitIndex => valuesByDriver
                .Select(values => values[bitIndex])
                .ToArray())
            .ToArray();
    }

    [Benchmark(Baseline = true)]
    public int ScalarOracle()
    {
        var checksum = 17;
        foreach (var drivers in scalarDriversByBit)
        {
            var resolution = NetResolver.Resolve(drivers);
            checksum = unchecked(
                (checksum * 31)
                + (int)resolution.Value
                + (int)resolution.Causes);
        }

        return checksum;
    }

    [Benchmark]
    public VectorNetResolution PackedKernel()
    {
        return VectorNetResolver.Resolve(Case.Width, vectorDrivers);
    }

    [Benchmark]
    public VectorNetResolution ProductionCallShape()
    {
        return VectorNetResolver.Resolve(
            Case.Width,
            driverOrdinals.Select(ordinal => vectorDrivers[ordinal]).ToArray());
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
