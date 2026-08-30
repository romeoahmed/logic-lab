using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("simulation", "advance")]
public class SimulationAdvanceBenchmarks
{
    private SimulationAdvanceWorkload workload = null!;
    private readonly AdvanceToNextQuiescentBoundary advance = new();

    [ParamsSource(nameof(Cases))]
    public CircuitBenchmarkCase Case { get; set; }

    public static IEnumerable<CircuitBenchmarkCase> Cases =>
        EngineBenchmarkCorpus.AdvanceCases;

    [GlobalSetup]
    public void Setup()
    {
        workload = EngineBenchmarkCorpus.CreateAdvanceWorkload(Case);
    }

    [Benchmark]
    public AdvanceCommitted OpenAdvanceAndClose()
    {
        var opened = EngineBenchmarkCorpus.Open(workload.OpenRequest);
        try
        {
            if (workload.Schedule is not null)
            {
                _ = (StimulusBatchScheduled)SimulationRuntime.Execute(
                    opened.Handle,
                    workload.Schedule,
                    CancellationToken.None);
            }

            return (AdvanceCommitted)SimulationRuntime.Execute(
                opened.Handle,
                advance,
                CancellationToken.None);
        }
        finally
        {
            _ = (SessionClosed)SimulationRuntime.Close(opened.Handle);
        }
    }
}
