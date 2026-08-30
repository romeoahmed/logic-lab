using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("simulation", "trace")]
public class SimulationTraceReadBenchmarks
{
    private SimulationTraceReadFixture fixture = null!;

    [Params(16, 256, 4096)]
    public int TransitionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        fixture = EngineBenchmarkCorpus.CreateTraceReadFixture(TransitionCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _ = (SessionClosed)SimulationRuntime.Close(fixture.Handle);
    }

    [Benchmark]
    public TraceTransitionsAvailable ReadTraceWindow() =>
        (TraceTransitionsAvailable)SimulationRuntime.Read(
            fixture.Handle,
            fixture.Query,
            CancellationToken.None);
}
