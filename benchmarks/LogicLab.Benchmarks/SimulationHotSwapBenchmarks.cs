using BenchmarkDotNet.Attributes;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Simulation;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("simulation", "hotswap-v1")]
public class SimulationHotSwapBenchmarks
{
    private OpenSimulationRequest request = null!;
    private HotSwapTo compatible = null!;
    private HotSwapTo incompatible = null!;

    [ParamsSource(nameof(Cases))]
    public CircuitBenchmarkCase Case { get; set; }

    public static IEnumerable<CircuitBenchmarkCase> Cases =>
    [
        new(CircuitBenchmarkShape.DFlipFlopBank, 256),
        new(CircuitBenchmarkShape.SinglePortRam, 4_096),
        new(CircuitBenchmarkShape.SinglePortRam, 4_096, wordWidth: 65),
    ];

    [GlobalSetup]
    public void Setup()
    {
        var circuit = BenchmarkCircuitCatalog.Create(Case);
        var original = EngineBenchmarkCorpus.Compile(circuit.Revision);
        var probes = circuit.ProbeNets.Select(net => original.SourceMap.Nets.Single(entry =>
            entry.Source.Identity is NetSourceIdentity identity && identity.NetId == net.Id).Source).ToArray();
        request = EngineBenchmarkCorpus.CreateOpenRequest(original, probes);
        var renamed = ((EditCommitted)ProjectEditor.Apply(circuit.Revision,
            new RenameCircuitDefinitionIntent(circuit.Revision.Document.EntryCircuitDefinitionId, "Hot Swap corpus"))).Revision;
        compatible = new(EngineBenchmarkCorpus.Compile(renamed), ulong.MaxValue, HotSwapConsumerBufferRequirements.None);
        var removed = ((EditCommitted)ProjectEditor.Apply(circuit.Revision,
            new RemoveComponentInstancesIntent(circuit.Revision.Document.EntryCircuitDefinitionId,
                [.. circuit.Revision.Document.EntryCircuitDefinition.ComponentInstances.Select(component => component.Id)]))).Revision;
        incompatible = new(EngineBenchmarkCorpus.Compile(removed), ulong.MaxValue, HotSwapConsumerBufferRequirements.None);
    }

    [Benchmark(Baseline = true)]
    public SessionClosed OpenClose()
    {
        var opened = EngineBenchmarkCorpus.Open(request);
        return (SessionClosed)SimulationRuntime.Close(opened.Handle);
    }

    [Benchmark]
    public HotSwapCommitted OpenMigrateAndClose() => (HotSwapCommitted)Execute(compatible);

    [Benchmark]
    public HotSwapIncompatible OpenRejectStateLossAndClose() => (HotSwapIncompatible)Execute(incompatible);

    private SimulationCommandOutcome Execute(HotSwapTo intent)
    {
        var opened = EngineBenchmarkCorpus.Open(request);
        try
        {
            return SimulationRuntime.Execute(opened.Handle, intent, CancellationToken.None);
        }
        finally
        {
            _ = (SessionClosed)SimulationRuntime.Close(opened.Handle);
        }
    }
}
