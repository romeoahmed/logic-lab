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
        int[] inputNetOrdinals,
        int[] outputDriverOrdinals,
        LogicVector? initialValue,
        IReadOnlyList<BitSlice>? slices = null,
        bool option = false,
        ClockSchedule? clockSchedule = null,
        SequentialEvaluatorOptions? sequentialOptions = null,
        IReadOnlyList<LogicVector>? initialMemory = null)
    {
        Ordinal = ordinal;
        Kind = kind;
        ContractKey = contractKey;
        Width = width;
        InputNetOrdinals = Array.AsReadOnly((int[])inputNetOrdinals.Clone());
        OutputDriverOrdinals = Array.AsReadOnly((int[])outputDriverOrdinals.Clone());
        InitialValue = initialValue;
        Slices = Array.AsReadOnly(
            slices is null ? [] : slices.ToArray());
        Option = option;
        ClockSchedule = clockSchedule;
        SequentialOptions = sequentialOptions;
        InitialMemory = initialMemory is null
            ? null
            : Array.AsReadOnly(initialMemory.ToArray());
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

    public ReadOnlyCollection<LogicVector>? InitialMemory { get; }
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
        SourceMapEntry[] evaluators,
        EvaluatorInputSourceMapEntry[] evaluatorInputs,
        SourceMapEntry[] drivers,
        SourceMapEntry[] nets,
        StronglyConnectedComponentMemberSourceMapEntry[]
            stronglyConnectedComponentMembers,
        SourceMapEntry[] netAliases)
    {
        Evaluators = Array.AsReadOnly((SourceMapEntry[])evaluators.Clone());
        EvaluatorInputs = Array.AsReadOnly(
            (EvaluatorInputSourceMapEntry[])evaluatorInputs.Clone());
        Drivers = Array.AsReadOnly((SourceMapEntry[])drivers.Clone());
        Nets = Array.AsReadOnly((SourceMapEntry[])nets.Clone());
        NetAliases = Array.AsReadOnly((SourceMapEntry[])netAliases.Clone());
        StronglyConnectedComponentMembers = Array.AsReadOnly(
            (StronglyConnectedComponentMemberSourceMapEntry[])
            stronglyConnectedComponentMembers.Clone());
        evaluatorOrdinals = Index(evaluators);
        driverOrdinals = Index(drivers);
        netOrdinals = Index(nets, netAliases);
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
