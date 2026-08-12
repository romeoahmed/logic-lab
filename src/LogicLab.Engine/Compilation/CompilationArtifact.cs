using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

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
    LogicUnsignedCompare,
    LogicAdder,
    LogicSubtractor,
    LogicShift,
    OutputSink,
    TopologySplit,
    TopologyConcat,
    TopologyZeroExtend,
    TopologySignExtend,
    ClockSource,
    SequentialDLatch,
    SequentialDff,
    SequentialRegister,
    SequentialSrLatch,
    SequentialJkff,
    SequentialTff,
    SequentialShiftRegister,
    SequentialCounter,
    MemoryRom,
    MemoryRamSinglePort,
}

internal static class SimulationEvaluatorKindFacts
{
    public static bool IsStateBoundary(SimulationEvaluatorKind kind)
    {
        return kind == SimulationEvaluatorKind.ClockSource || IsSequential(kind);
    }

    public static bool IsSequential(SimulationEvaluatorKind kind)
    {
        return kind is SimulationEvaluatorKind.SequentialDLatch
            or SimulationEvaluatorKind.SequentialDff
            or SimulationEvaluatorKind.SequentialRegister
            or SimulationEvaluatorKind.SequentialSrLatch
            or SimulationEvaluatorKind.SequentialJkff
            or SimulationEvaluatorKind.SequentialTff
            or SimulationEvaluatorKind.SequentialShiftRegister
            or SimulationEvaluatorKind.SequentialCounter;
    }

    public static bool IsMemory(SimulationEvaluatorKind kind)
    {
        return kind is SimulationEvaluatorKind.MemoryRom
            or SimulationEvaluatorKind.MemoryRamSinglePort;
    }

    public static bool IsTriggeredState(SimulationEvaluatorKind kind)
    {
        return IsSequential(kind)
            || kind == SimulationEvaluatorKind.MemoryRamSinglePort;
    }

    public static bool ConsumesNetCombinationally(
        SimulationEvaluator evaluator,
        int netOrdinal)
    {
        return evaluator.Kind != SimulationEvaluatorKind.MemoryRamSinglePort
            || evaluator.InputNetOrdinals[0] == netOrdinal;
    }
}

internal sealed record ClockSchedule(
    ulong FirstTransition,
    ulong HighDuration,
    ulong LowDuration);

internal enum SequentialDirection
{
    None,
    TowardHigh,
    TowardLow,
    Up,
    Down,
}

internal sealed record SequentialEvaluatorOptions(
    int? ClockInputOrdinal,
    bool RisingEdge,
    SequentialDirection Direction);

internal sealed class SimulationEvaluator
{
    internal SimulationEvaluator(
        int ordinal,
        SimulationEvaluatorKind kind,
        ComponentContractKey contractKey,
        uint width,
        int[] ownedInputNetOrdinals,
        int[] ownedOutputDriverOrdinals,
        LogicVector? initialValue,
        IReadOnlyList<BitSlice>? slices = null,
        bool option = false,
        ClockSchedule? clockSchedule = null,
        SequentialEvaluatorOptions? sequentialOptions = null,
        PackedMemory? initialMemory = null)
    {
        Ordinal = ordinal;
        Kind = kind;
        ContractKey = contractKey;
        Width = width;
        InputNetOrdinals = Array.AsReadOnly(ownedInputNetOrdinals);
        OutputDriverOrdinals = Array.AsReadOnly(ownedOutputDriverOrdinals);
        InitialValue = initialValue;
        Slices = Array.AsReadOnly(
            slices is null ? [] : slices.ToArray());
        Option = option;
        ClockSchedule = clockSchedule;
        SequentialOptions = sequentialOptions;
        InitialMemory = initialMemory;
    }

    public int Ordinal { get; }

    public SimulationEvaluatorKind Kind { get; }

    public ComponentContractKey ContractKey { get; }

    public uint Width { get; }

    public ReadOnlyCollection<int> InputNetOrdinals { get; }

    public ReadOnlyCollection<int> OutputDriverOrdinals { get; }

    public LogicVector? InitialValue { get; }

    public ReadOnlyCollection<BitSlice> Slices { get; }

    public bool Option { get; }

    public ClockSchedule? ClockSchedule { get; }

    public SequentialEvaluatorOptions? SequentialOptions { get; }

    public PackedMemory? InitialMemory { get; }
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
        int[] ownedDriverOrdinals,
        int[] ownedReceiverEvaluatorOrdinals)
    {
        Ordinal = ordinal;
        Width = width;
        DriverOrdinals = Array.AsReadOnly(ownedDriverOrdinals);
        ReceiverEvaluatorOrdinals = Array.AsReadOnly(ownedReceiverEvaluatorOrdinals);
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
        int[] ownedEvaluatorOrdinals,
        bool isCyclic)
    {
        Ordinal = ordinal;
        EvaluatorOrdinals = Array.AsReadOnly(ownedEvaluatorOrdinals);
        IsCyclic = isCyclic;
    }

    public int Ordinal { get; }

    public ReadOnlyCollection<int> EvaluatorOrdinals { get; }

    public bool IsCyclic { get; }
}

internal sealed class SimulationIr
{
    internal SimulationIr(
        SimulationEvaluator[] ownedEvaluators,
        SimulationDriver[] ownedDrivers,
        SimulationNet[] ownedNets,
        int[] ownedFanoutOffsets,
        int[] ownedFanoutEvaluatorOrdinals,
        CombinationalStronglyConnectedComponent[] ownedStronglyConnectedComponents,
        int[] ownedCondensationOrder)
    {
        Evaluators = Array.AsReadOnly(ownedEvaluators);
        Drivers = Array.AsReadOnly(ownedDrivers);
        Nets = Array.AsReadOnly(ownedNets);
        FanoutOffsets = Array.AsReadOnly(ownedFanoutOffsets);
        FanoutEvaluatorOrdinals = Array.AsReadOnly(ownedFanoutEvaluatorOrdinals);
        StronglyConnectedComponents = Array.AsReadOnly(
            ownedStronglyConnectedComponents);
        CondensationOrder = Array.AsReadOnly(ownedCondensationOrder);
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

public sealed record HierarchyPath
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

    public bool Equals(HierarchyPath? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && EntryCircuitDefinitionId == other.EntryCircuitDefinitionId
                && Steps.SequenceEqual(other.Steps));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EntryCircuitDefinitionId);
        foreach (var step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }
}

public sealed record CompilationSource
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

    public bool Equals(CompilationSource? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && Identity == other.Identity
                && HierarchyPath.Equals(other.HierarchyPath));
    }

    public override int GetHashCode() => HashCode.Combine(Identity, HierarchyPath);
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
    private readonly Dictionary<CompilationSource, int> evaluatorOrdinals;
    private readonly Dictionary<CompilationSource, int> driverOrdinals;
    private readonly Dictionary<CompilationSource, int> netOrdinals;

    internal SourceMap(
        SourceMapEntry[] ownedEvaluators,
        EvaluatorInputSourceMapEntry[] ownedEvaluatorInputs,
        SourceMapEntry[] ownedDrivers,
        SourceMapEntry[] ownedNets,
        StronglyConnectedComponentMemberSourceMapEntry[]
            ownedStronglyConnectedComponentMembers,
        SourceMapEntry[] ownedNetAliases)
    {
        Evaluators = Array.AsReadOnly(ownedEvaluators);
        EvaluatorInputs = Array.AsReadOnly(ownedEvaluatorInputs);
        Drivers = Array.AsReadOnly(ownedDrivers);
        Nets = Array.AsReadOnly(ownedNets);
        NetAliases = Array.AsReadOnly(ownedNetAliases);
        StronglyConnectedComponentMembers = Array.AsReadOnly(
            ownedStronglyConnectedComponentMembers);
        evaluatorOrdinals = Index(ownedEvaluators);
        driverOrdinals = Index(ownedDrivers);
        netOrdinals = Index(ownedNets, ownedNetAliases);
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
        ArgumentNullException.ThrowIfNull(source);
        return netOrdinals.TryGetValue(source, out ordinal);
    }

    public bool TryGetDriverOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        return driverOrdinals.TryGetValue(source, out ordinal);
    }

    internal bool TryGetEvaluatorOrdinal(
        CompilationSource source,
        out int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        return evaluatorOrdinals.TryGetValue(source, out ordinal);
    }

    private static Dictionary<CompilationSource, int> Index(
        SourceMapEntry[] entries,
        SourceMapEntry[]? aliases = null)
    {
        var index = new Dictionary<CompilationSource, int>(checked(
            entries.Length + (aliases?.Length ?? 0)));
        AddEntries(entries);
        if (aliases is not null)
        {
            AddEntries(aliases);
        }

        return index;

        void AddEntries(SourceMapEntry[] additions)
        {
            foreach (var entry in additions)
            {
                index.TryAdd(entry.Source, entry.Ordinal);
            }
        }
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
