using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

public sealed record CompilationArtifactKey(
    ProjectRevisionId ProjectRevisionId,
    CircuitDefinitionId EntryCircuitDefinitionId,
    string LibrarySnapshotFingerprint,
    string CompilerSemanticVersion);

internal enum SimulationEvaluatorKind
{
    InputSource,
    ConstantSource,
    LogicNot,
    LogicAnd,
    LogicNand,
    LogicOr,
    LogicNor,
    LogicXor,
    LogicXnor,
    LogicBuffer,
    LogicTristate,
    LogicMux,
    LogicDemux,
    LogicDecoder,
    LogicPriorityEncoder,
    OutputSink,
    TopologySplit,
    TopologyConcat,
    TopologyZeroExtend,
    TopologySignExtend,
}

internal sealed class SimulationEvaluator
{
    internal SimulationEvaluator(
        int ordinal,
        SimulationEvaluatorKind kind,
        uint width,
        int[] inputNetOrdinals,
        int[] outputDriverOrdinals,
        LogicVector? initialValue,
        IReadOnlyList<BitSlice>? slices = null,
        bool option = false)
    {
        Ordinal = ordinal;
        Kind = kind;
        Width = width;
        InputNetOrdinals = Array.AsReadOnly((int[])inputNetOrdinals.Clone());
        OutputDriverOrdinals = Array.AsReadOnly((int[])outputDriverOrdinals.Clone());
        InitialValue = initialValue;
        Slices = Array.AsReadOnly(
            slices is null ? [] : slices.ToArray());
        Option = option;
    }

    public int Ordinal { get; }

    public SimulationEvaluatorKind Kind { get; }

    public uint Width { get; }

    public ReadOnlyCollection<int> InputNetOrdinals { get; }

    public ReadOnlyCollection<int> OutputDriverOrdinals { get; }

    public LogicVector? InitialValue { get; }

    public ReadOnlyCollection<BitSlice> Slices { get; }

    public bool Option { get; }
}

internal sealed record SimulationDriver(
    int Ordinal,
    int EvaluatorOrdinal,
    int? NetOrdinal,
    uint Width);

internal sealed class SimulationNet
{
    internal SimulationNet(
        int ordinal,
        uint width,
        int[] driverOrdinals,
        int[] receiverEvaluatorOrdinals)
    {
        Ordinal = ordinal;
        Width = width;
        DriverOrdinals = Array.AsReadOnly((int[])driverOrdinals.Clone());
        ReceiverEvaluatorOrdinals = Array.AsReadOnly(
            (int[])receiverEvaluatorOrdinals.Clone());
    }

    public int Ordinal { get; }

    public uint Width { get; }

    public ReadOnlyCollection<int> DriverOrdinals { get; }

    public ReadOnlyCollection<int> ReceiverEvaluatorOrdinals { get; }
}

internal sealed class CombinationalStronglyConnectedComponent
{
    internal CombinationalStronglyConnectedComponent(
        int ordinal,
        int[] evaluatorOrdinals,
        bool isCyclic)
    {
        Ordinal = ordinal;
        EvaluatorOrdinals = Array.AsReadOnly((int[])evaluatorOrdinals.Clone());
        IsCyclic = isCyclic;
    }

    public int Ordinal { get; }

    public ReadOnlyCollection<int> EvaluatorOrdinals { get; }

    public bool IsCyclic { get; }
}

internal sealed class SimulationIr
{
    internal SimulationIr(
        SimulationEvaluator[] evaluators,
        SimulationDriver[] drivers,
        SimulationNet[] nets,
        int[] fanoutOffsets,
        int[] fanoutEvaluatorOrdinals,
        CombinationalStronglyConnectedComponent[] stronglyConnectedComponents,
        int[] condensationOrder)
    {
        Evaluators = Array.AsReadOnly((SimulationEvaluator[])evaluators.Clone());
        Drivers = Array.AsReadOnly((SimulationDriver[])drivers.Clone());
        Nets = Array.AsReadOnly((SimulationNet[])nets.Clone());
        FanoutOffsets = Array.AsReadOnly((int[])fanoutOffsets.Clone());
        FanoutEvaluatorOrdinals = Array.AsReadOnly(
            (int[])fanoutEvaluatorOrdinals.Clone());
        StronglyConnectedComponents = Array.AsReadOnly(
            (CombinationalStronglyConnectedComponent[])
            stronglyConnectedComponents.Clone());
        CondensationOrder = Array.AsReadOnly((int[])condensationOrder.Clone());
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
    public HierarchyPath(
        CircuitDefinitionId entryCircuitDefinitionId,
        IReadOnlyList<HierarchyPathStep> steps)
    {
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionId);
        ArgumentNullException.ThrowIfNull(steps);
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        Steps = Array.AsReadOnly(steps.ToArray());
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
    internal SourceMap(
        SourceMapEntry[] evaluators,
        EvaluatorInputSourceMapEntry[] evaluatorInputs,
        SourceMapEntry[] drivers,
        SourceMapEntry[] nets,
        StronglyConnectedComponentMemberSourceMapEntry[]
            stronglyConnectedComponentMembers,
        SourceMapEntry[]? netAliases = null)
    {
        Evaluators = Array.AsReadOnly((SourceMapEntry[])evaluators.Clone());
        EvaluatorInputs = Array.AsReadOnly(
            (EvaluatorInputSourceMapEntry[])evaluatorInputs.Clone());
        Drivers = Array.AsReadOnly((SourceMapEntry[])drivers.Clone());
        Nets = Array.AsReadOnly((SourceMapEntry[])nets.Clone());
        NetAliases = Array.AsReadOnly(
            netAliases is null ? [] : (SourceMapEntry[])netAliases.Clone());
        StronglyConnectedComponentMembers = Array.AsReadOnly(
            (StronglyConnectedComponentMemberSourceMapEntry[])
            stronglyConnectedComponentMembers.Clone());
    }

    public ReadOnlyCollection<SourceMapEntry> Evaluators { get; }

    public ReadOnlyCollection<EvaluatorInputSourceMapEntry> EvaluatorInputs { get; }

    public ReadOnlyCollection<SourceMapEntry> Drivers { get; }

    public ReadOnlyCollection<SourceMapEntry> Nets { get; }

    public ReadOnlyCollection<SourceMapEntry> NetAliases { get; }

    public ReadOnlyCollection<StronglyConnectedComponentMemberSourceMapEntry>
        StronglyConnectedComponentMembers
    { get; }

    public bool TryGetNetOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        return TryGetOrdinal(Nets, source, out ordinal)
            || TryGetOrdinal(NetAliases, source, out ordinal);
    }

    public bool TryGetDriverOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        return TryGetOrdinal(Drivers, source, out ordinal);
    }

    private static bool TryGetOrdinal(
        ReadOnlyCollection<SourceMapEntry> entries,
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var entry in entries)
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

    internal SimulationIr SimulationIr { get; }

    public SourceMap SourceMap { get; }

    internal ProjectRevision SourceRevision { get; }
}
