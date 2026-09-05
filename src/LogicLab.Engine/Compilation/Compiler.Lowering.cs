using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

public static partial class Compiler
{
    private static SimulationEvaluatorKind? GetEvaluatorKind(ComponentContractKey key) =>
        key.ContractId switch
        {
            "source.input" => SimulationEvaluatorKind.InputSource,
            "source.constant" => SimulationEvaluatorKind.ConstantSource,
            "source.clock" => SimulationEvaluatorKind.ClockSource,
            "logic.not" => SimulationEvaluatorKind.LogicNot,
            "logic.buffer" => SimulationEvaluatorKind.LogicBuffer,
            "logic.and" => SimulationEvaluatorKind.LogicAnd,
            "logic.nand" => SimulationEvaluatorKind.LogicNand,
            "logic.or" => SimulationEvaluatorKind.LogicOr,
            "logic.nor" => SimulationEvaluatorKind.LogicNor,
            "logic.xor" => SimulationEvaluatorKind.LogicXor,
            "logic.xnor" => SimulationEvaluatorKind.LogicXnor,
            "logic.tristate" => SimulationEvaluatorKind.LogicTristate,
            "logic.mux" => SimulationEvaluatorKind.LogicMux,
            "logic.demux" => SimulationEvaluatorKind.LogicDemux,
            "logic.decoder" => SimulationEvaluatorKind.LogicDecoder,
            "logic.priority_encoder" => SimulationEvaluatorKind.LogicPriorityEncoder,
            "logic.unsigned_compare" => SimulationEvaluatorKind.LogicUnsignedCompare,
            "logic.adder" => SimulationEvaluatorKind.LogicAdder,
            "logic.subtractor" => SimulationEvaluatorKind.LogicSubtractor,
            "logic.shift" => SimulationEvaluatorKind.LogicShift,
            "sink.output" => SimulationEvaluatorKind.OutputSink,
            "topology.split" => SimulationEvaluatorKind.TopologySplit,
            "topology.concat" => SimulationEvaluatorKind.TopologyConcat,
            "topology.zero_extend" => SimulationEvaluatorKind.TopologyZeroExtend,
            "topology.sign_extend" => SimulationEvaluatorKind.TopologySignExtend,
            "sequential.d_latch" => SimulationEvaluatorKind.SequentialDLatch,
            "sequential.dff" => SimulationEvaluatorKind.SequentialDff,
            "sequential.register" => SimulationEvaluatorKind.SequentialRegister,
            "sequential.sr_latch" => SimulationEvaluatorKind.SequentialSrLatch,
            "sequential.jkff" => SimulationEvaluatorKind.SequentialJkff,
            "sequential.tff" => SimulationEvaluatorKind.SequentialTff,
            "sequential.shift_register" => SimulationEvaluatorKind.SequentialShiftRegister,
            "sequential.counter" => SimulationEvaluatorKind.SequentialCounter,
            "memory.rom" => SimulationEvaluatorKind.MemoryRom,
            "memory.ram_single_port" => SimulationEvaluatorKind.MemoryRamSinglePort,
            _ => null,
        };

    private static bool TryResolvePorts(
        ComponentInstance instance,
        ComponentContractSchema schema,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out ComponentPortResolution? resolution)
    {
        try
        {
            resolution = schema.ResolvePorts(
                instance.Parameters,
                cancellationToken);
            return true;
        }
        catch (ArgumentException)
        {
            resolution = null;
            return false;
        }
    }

    private static uint GetEvaluatorWidth(
        SimulationEvaluatorKind kind,
        ResolvedComponentPortSchema[] ports)
    {
        var primaryPortId = kind switch
        {
            SimulationEvaluatorKind.OutputSink
                or SimulationEvaluatorKind.TopologySplit
                or SimulationEvaluatorKind.LogicDemux => "D",
            SimulationEvaluatorKind.LogicDecoder => "Q0",
            SimulationEvaluatorKind.LogicUnsignedCompare => "LT",
            SimulationEvaluatorKind.LogicAdder => "SUM",
            SimulationEvaluatorKind.LogicSubtractor => "DIFF",
            _ => "Q",
        };
        return ports.Single(port => string.Equals(
            port.Id,
            primaryPortId,
            StringComparison.Ordinal)).Width;
    }

    private static LogicVector? GetInitialValue(
        SimulationEvaluatorKind kind,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        var parameterId = SimulationEvaluatorKindFacts.IsSequential(kind)
            ? "initialState"
            : kind switch
            {
                SimulationEvaluatorKind.InputSource => "initialValue",
                SimulationEvaluatorKind.ConstantSource => "value",
                SimulationEvaluatorKind.ClockSource => "initialValue",
                _ => null,
            };
        if (parameterId is null)
        {
            return null;
        }

        var values = (LogicVectorParameterValue)parameters.Single(binding =>
            string.Equals(
                binding.ParameterId,
                parameterId,
                StringComparison.Ordinal)).Value;
        return new LogicVector(values.Values);
    }

    private static ReadOnlyCollection<BitSlice>? GetSlices(
        SimulationEvaluatorKind kind,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        return kind == SimulationEvaluatorKind.TopologySplit
            ? ((SlicesParameterValue)parameters.Single(binding => string.Equals(
                binding.ParameterId,
                "slices",
                StringComparison.Ordinal)).Value).Values
            : null;
    }

    private static bool GetOption(
        SimulationEvaluatorKind kind,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        var (parameterId, trueValue) = kind switch
        {
            SimulationEvaluatorKind.LogicTristate or SimulationEvaluatorKind.LogicDecoder =>
                ("enablePolarity", "activeHigh"),
            SimulationEvaluatorKind.LogicPriorityEncoder =>
                ("priority", "lowestIndex"),
            SimulationEvaluatorKind.LogicShift =>
                ("direction", "left"),
            _ => (null, null),
        };
        if (parameterId is null)
        {
            return false;
        }

        var choice = (ChoiceParameterValue)parameters.Single(binding => string.Equals(
            binding.ParameterId,
            parameterId,
            StringComparison.Ordinal)).Value;
        return string.Equals(choice.Value, trueValue, StringComparison.Ordinal);
    }

    private static ClockSchedule? GetClockSchedule(
        SimulationEvaluatorKind kind,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        if (kind != SimulationEvaluatorKind.ClockSource)
        {
            return null;
        }

        return new ClockSchedule(
            Unsigned64(parameters, "firstTransition"),
            Unsigned64(parameters, "highDuration"),
            Unsigned64(parameters, "lowDuration"));
    }

    private static SequentialEvaluatorOptions? GetSequentialOptions(
        SimulationEvaluatorKind kind,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        if (!SimulationEvaluatorKindFacts.IsSequential(kind))
        {
            return null;
        }

        int? clockInputOrdinal = kind switch
        {
            SimulationEvaluatorKind.SequentialDLatch
                or SimulationEvaluatorKind.SequentialSrLatch => null,
            SimulationEvaluatorKind.SequentialDff
                or SimulationEvaluatorKind.SequentialRegister
                or SimulationEvaluatorKind.SequentialTff => 1,
            SimulationEvaluatorKind.SequentialJkff
                or SimulationEvaluatorKind.SequentialCounter => 2,
            SimulationEvaluatorKind.SequentialShiftRegister => 3,
            _ => throw new InvalidOperationException(
                "The sequential evaluator kind is undefined."),
        };
        var risingEdge = clockInputOrdinal is null
            || Choice(parameters, "edge") == "rising";
        var direction = kind switch
        {
            SimulationEvaluatorKind.SequentialShiftRegister =>
                Choice(parameters, "direction") == "towardHigh"
                    ? SequentialDirection.TowardHigh
                    : SequentialDirection.TowardLow,
            SimulationEvaluatorKind.SequentialCounter =>
                Choice(parameters, "direction") == "up"
                    ? SequentialDirection.Up
                    : SequentialDirection.Down,
            _ => SequentialDirection.None,
        };
        return new SequentialEvaluatorOptions(
            clockInputOrdinal,
            risingEdge,
            direction);
    }

    private static PackedMemory? GetInitialMemory(
        ProjectDocument document,
        SimulationEvaluatorKind kind,
        IReadOnlyList<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken)
    {
        if (!SimulationEvaluatorKindFacts.IsMemory(kind))
        {
            return null;
        }

        var reference = (MemoryImageParameterValue)parameters.Single(binding =>
            string.Equals(
                binding.ParameterId,
                "initialImage",
                StringComparison.Ordinal)).Value;
        var image = document.FindMemoryImage(reference.MemoryImageId)
            ?? throw new InvalidOperationException(
                "A compiled memory evaluator references a missing Memory Image.");
        return PackedMemory.FromImage(image, cancellationToken);
    }

    private static ulong CountMemoryCells(
        ProjectDocument document,
        IEnumerable<ComponentInstance> instances)
    {
        ulong cells = 0;
        foreach (var instance in instances)
        {
            var reference = (MemoryImageParameterValue)instance.Parameters.Single(binding =>
                string.Equals(
                    binding.ParameterId,
                    "initialImage",
                    StringComparison.Ordinal)).Value;
            var image = document.FindMemoryImage(reference.MemoryImageId)
                ?? throw new InvalidOperationException(
                    "A compiled memory evaluator references a missing Memory Image.");
            cells = checked(cells + ((ulong)image.Width * image.Depth));
        }

        return cells;
    }

    private static string Choice(
        IEnumerable<ComponentParameterBinding> parameters,
        string parameterId)
    {
        return ((ChoiceParameterValue)parameters.Single(binding => string.Equals(
            binding.ParameterId,
            parameterId,
            StringComparison.Ordinal)).Value).Value;
    }

    private static ulong Unsigned64(
        IEnumerable<ComponentParameterBinding> parameters,
        string parameterId)
    {
        return ((Unsigned64ParameterValue)parameters.Single(binding => string.Equals(
            binding.ParameterId,
            parameterId,
            StringComparison.Ordinal)).Value).Value;
    }
}
