using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class SimulationSessionBenchmarks
{
    private OpenSimulationRequest request = null!;

    [Params(1, 32, 256)]
    public int GateCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        request = BenchmarkCircuitFactory.CreateOpenRequest(GateCount);
    }

    [Benchmark]
    public SimulationOpenOutcome OpenAndSettle() =>
        SimulationRuntime.Open(request, CancellationToken.None);
}
