using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

public sealed record CompilationArtifactKey(
    ProjectRevisionId ProjectRevisionId,
    CircuitDefinitionId EntryCircuitDefinitionId,
    string LibrarySnapshotFingerprint,
    string CompilerSemanticVersion);

public enum SimulationEvaluatorKind
{
    InputSource,
    LogicNot,
    OutputSink,
}

public sealed class SimulationEvaluator
{
    private readonly int[] inputNetOrdinals;
    private readonly int[] outputDriverOrdinals;

    internal SimulationEvaluator(
        int ordinal,
        SimulationEvaluatorKind kind,
        uint width,
        int[] inputNetOrdinals,
        int[] outputDriverOrdinals,
        LogicVector? initialValue)
    {
        Ordinal = ordinal;
        Kind = kind;
        Width = width;
        this.inputNetOrdinals = (int[])inputNetOrdinals.Clone();
        this.outputDriverOrdinals = (int[])outputDriverOrdinals.Clone();
        InputNetOrdinals = Array.AsReadOnly(this.inputNetOrdinals);
        OutputDriverOrdinals = Array.AsReadOnly(this.outputDriverOrdinals);
        InitialValue = initialValue;
    }

    public int Ordinal { get; }

    public SimulationEvaluatorKind Kind { get; }

    public uint Width { get; }

    public ReadOnlyCollection<int> InputNetOrdinals { get; }

    public ReadOnlyCollection<int> OutputDriverOrdinals { get; }

    public LogicVector? InitialValue { get; }
}

public sealed record SimulationDriver(
    int Ordinal,
    int EvaluatorOrdinal,
    int? NetOrdinal,
    uint Width);

public sealed class SimulationNet
{
    private readonly int[] driverOrdinals;
    private readonly int[] receiverEvaluatorOrdinals;

    internal SimulationNet(
        int ordinal,
        uint width,
        int[] driverOrdinals,
        int[] receiverEvaluatorOrdinals)
    {
        Ordinal = ordinal;
        Width = width;
        this.driverOrdinals = (int[])driverOrdinals.Clone();
        this.receiverEvaluatorOrdinals = (int[])receiverEvaluatorOrdinals.Clone();
        DriverOrdinals = Array.AsReadOnly(this.driverOrdinals);
        ReceiverEvaluatorOrdinals = Array.AsReadOnly(this.receiverEvaluatorOrdinals);
    }

    public int Ordinal { get; }

    public uint Width { get; }

    public ReadOnlyCollection<int> DriverOrdinals { get; }

    public ReadOnlyCollection<int> ReceiverEvaluatorOrdinals { get; }
}

public sealed class CombinationalStronglyConnectedComponent
{
    private readonly int[] evaluatorOrdinals;

    internal CombinationalStronglyConnectedComponent(
        int ordinal,
        int[] evaluatorOrdinals,
        bool isCyclic)
    {
        Ordinal = ordinal;
        this.evaluatorOrdinals = (int[])evaluatorOrdinals.Clone();
        EvaluatorOrdinals = Array.AsReadOnly(this.evaluatorOrdinals);
        IsCyclic = isCyclic;
    }

    public int Ordinal { get; }

    public ReadOnlyCollection<int> EvaluatorOrdinals { get; }

    public bool IsCyclic { get; }
}

public sealed class SimulationIr
{
    private readonly SimulationEvaluator[] evaluators;
    private readonly SimulationDriver[] drivers;
    private readonly SimulationNet[] nets;
    private readonly int[] fanoutOffsets;
    private readonly int[] fanoutEvaluatorOrdinals;
    private readonly CombinationalStronglyConnectedComponent[] stronglyConnectedComponents;
    private readonly int[] condensationOrder;

    internal SimulationIr(
        SimulationEvaluator[] evaluators,
        SimulationDriver[] drivers,
        SimulationNet[] nets,
        int[] fanoutOffsets,
        int[] fanoutEvaluatorOrdinals,
        CombinationalStronglyConnectedComponent[] stronglyConnectedComponents,
        int[] condensationOrder)
    {
        this.evaluators = (SimulationEvaluator[])evaluators.Clone();
        this.drivers = (SimulationDriver[])drivers.Clone();
        this.nets = (SimulationNet[])nets.Clone();
        this.fanoutOffsets = (int[])fanoutOffsets.Clone();
        this.fanoutEvaluatorOrdinals = (int[])fanoutEvaluatorOrdinals.Clone();
        this.stronglyConnectedComponents =
            (CombinationalStronglyConnectedComponent[])stronglyConnectedComponents.Clone();
        this.condensationOrder = (int[])condensationOrder.Clone();
        Evaluators = Array.AsReadOnly(this.evaluators);
        Drivers = Array.AsReadOnly(this.drivers);
        Nets = Array.AsReadOnly(this.nets);
        FanoutOffsets = Array.AsReadOnly(this.fanoutOffsets);
        FanoutEvaluatorOrdinals = Array.AsReadOnly(this.fanoutEvaluatorOrdinals);
        StronglyConnectedComponents = Array.AsReadOnly(this.stronglyConnectedComponents);
        CondensationOrder = Array.AsReadOnly(this.condensationOrder);
    }

    public ReadOnlyCollection<SimulationEvaluator> Evaluators { get; }

    public ReadOnlyCollection<SimulationDriver> Drivers { get; }

    public ReadOnlyCollection<SimulationNet> Nets { get; }

    public ReadOnlyCollection<int> FanoutOffsets { get; }

    public ReadOnlyCollection<int> FanoutEvaluatorOrdinals { get; }

    public ReadOnlyCollection<CombinationalStronglyConnectedComponent>
        StronglyConnectedComponents
    { get; }

    public ReadOnlyCollection<int> CondensationOrder { get; }
}

public sealed record HierarchyPathStep(
    CircuitDefinitionId ContainingCircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId);

public sealed class HierarchyPath
{
    private readonly HierarchyPathStep[] steps;

    public HierarchyPath(
        CircuitDefinitionId entryCircuitDefinitionId,
        IReadOnlyList<HierarchyPathStep> steps)
    {
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionId);
        ArgumentNullException.ThrowIfNull(steps);
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        this.steps = steps.ToArray();
        Steps = Array.AsReadOnly(this.steps);
    }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public ReadOnlyCollection<HierarchyPathStep> Steps { get; }
}

public sealed class CompilationSource
{
    public CompilationSource(
        AuthoredSourceIdentity identity,
        HierarchyPath hierarchyPath)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(hierarchyPath);
        Identity = identity;
        HierarchyPath = hierarchyPath;
    }

    public AuthoredSourceIdentity Identity { get; }

    public HierarchyPath HierarchyPath { get; }
}

public sealed record SourceMapEntry(
    int Ordinal,
    CompilationSource Source);

public sealed record EvaluatorInputSourceMapEntry(
    int EvaluatorOrdinal,
    int InputOrdinal,
    CompilationSource Source);

public sealed record StronglyConnectedComponentMemberSourceMapEntry(
    int StronglyConnectedComponentOrdinal,
    int EvaluatorOrdinal,
    CompilationSource Source);

public sealed class SourceMap
{
    private readonly SourceMapEntry[] evaluators;
    private readonly EvaluatorInputSourceMapEntry[] evaluatorInputs;
    private readonly SourceMapEntry[] drivers;
    private readonly SourceMapEntry[] nets;
    private readonly StronglyConnectedComponentMemberSourceMapEntry[]
        stronglyConnectedComponentMembers;

    internal SourceMap(
        SourceMapEntry[] evaluators,
        EvaluatorInputSourceMapEntry[] evaluatorInputs,
        SourceMapEntry[] drivers,
        SourceMapEntry[] nets,
        StronglyConnectedComponentMemberSourceMapEntry[]
            stronglyConnectedComponentMembers)
    {
        this.evaluators = (SourceMapEntry[])evaluators.Clone();
        this.evaluatorInputs = (EvaluatorInputSourceMapEntry[])evaluatorInputs.Clone();
        this.drivers = (SourceMapEntry[])drivers.Clone();
        this.nets = (SourceMapEntry[])nets.Clone();
        this.stronglyConnectedComponentMembers =
            (StronglyConnectedComponentMemberSourceMapEntry[])
            stronglyConnectedComponentMembers.Clone();
        Evaluators = Array.AsReadOnly(this.evaluators);
        EvaluatorInputs = Array.AsReadOnly(this.evaluatorInputs);
        Drivers = Array.AsReadOnly(this.drivers);
        Nets = Array.AsReadOnly(this.nets);
        StronglyConnectedComponentMembers =
            Array.AsReadOnly(this.stronglyConnectedComponentMembers);
    }

    public ReadOnlyCollection<SourceMapEntry> Evaluators { get; }

    public ReadOnlyCollection<EvaluatorInputSourceMapEntry> EvaluatorInputs { get; }

    public ReadOnlyCollection<SourceMapEntry> Drivers { get; }

    public ReadOnlyCollection<SourceMapEntry> Nets { get; }

    public ReadOnlyCollection<StronglyConnectedComponentMemberSourceMapEntry>
        StronglyConnectedComponentMembers
    { get; }

    public bool TryGetNetOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var entry in nets)
        {
            if (entry.Source.Identity == source.Identity
                && PathsEqual(entry.Source.HierarchyPath, source.HierarchyPath))
            {
                ordinal = entry.Ordinal;
                return true;
            }
        }

        ordinal = default;
        return false;
    }

    private static bool PathsEqual(HierarchyPath left, HierarchyPath right)
    {
        return left.EntryCircuitDefinitionId == right.EntryCircuitDefinitionId
            && left.Steps.SequenceEqual(right.Steps);
    }
}

public sealed class CompilationArtifact
{
    internal CompilationArtifact(
        CompilationArtifactKey key,
        SimulationIr simulationIr,
        SourceMap sourceMap,
        ProjectRevision sourceRevision)
    {
        Key = key;
        SimulationIr = simulationIr;
        SourceMap = sourceMap;
        SourceRevision = sourceRevision;
    }

    public CompilationArtifactKey Key { get; }

    public SimulationIr SimulationIr { get; }

    public SourceMap SourceMap { get; }

    internal ProjectRevision SourceRevision { get; }
}
