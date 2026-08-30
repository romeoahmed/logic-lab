using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("simulation", "read")]
public class SimulationSnapshotReadBenchmarks
{
    private SimulationSessionHandle handle = null!;
    private readonly ReadSessionSnapshot query = new();

    [ParamsSource(nameof(Cases))]
    public CircuitBenchmarkCase Case { get; set; }

    public static IEnumerable<CircuitBenchmarkCase> Cases =>
        EngineBenchmarkCorpus.SnapshotCases;

    [GlobalSetup]
    public void Setup()
    {
        handle = EngineBenchmarkCorpus.Open(
            EngineBenchmarkCorpus.CreateOpenRequest(Case)).Handle;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _ = (SessionClosed)SimulationRuntime.Close(handle);
    }

    [Benchmark]
    public SessionSnapshotRead ReadSnapshot() =>
        (SessionSnapshotRead)SimulationRuntime.Read(
            handle,
            query,
            CancellationToken.None);
}
