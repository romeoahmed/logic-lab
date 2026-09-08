using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

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
