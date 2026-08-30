using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("simulation", "open")]
public class SimulationOpenBenchmarks
{
    private OpenSimulationRequest request = null!;

    [ParamsSource(nameof(Cases))]
    public CircuitBenchmarkCase Case { get; set; }

    public static IEnumerable<CircuitBenchmarkCase> Cases =>
        EngineBenchmarkCorpus.CompilationCases;

    [GlobalSetup]
    public void Setup()
    {
        request = EngineBenchmarkCorpus.CreateOpenRequest(Case);
    }

    [Benchmark]
    public SessionClosed OpenSettleAndClose()
    {
        var opened = EngineBenchmarkCorpus.Open(request);
        return (SessionClosed)SimulationRuntime.Close(opened.Handle);
    }
}
