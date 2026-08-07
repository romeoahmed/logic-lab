using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal static class SettlementOwnedBufferAccounting
{
    public static ulong PeakOwnedBufferBytes(SimulationIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        return checked(
            SettlementScratch.OwnedBufferBytes(ir)
            + Math.Max(
                PeakEvaluatorTemporaryOwnedBufferBytes(ir),
                PeakRecomputedNetResolutionOwnedBufferBytes(ir)));
    }

    private static ulong PeakRecomputedNetResolutionOwnedBufferBytes(SimulationIr ir)
    {
        ulong peakBytes = 0;
        foreach (var evaluator in ir.Evaluators)
        {
            if (!IsEvaluatedDuringSettlement(evaluator.Kind))
            {
                continue;
            }

            foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
            {
                if (ir.Drivers[driverOrdinal].NetOrdinal is { } netOrdinal)
                {
                    peakBytes = Math.Max(
                        peakBytes,
                        NetResolutionPlaneBytes(ir.Nets[netOrdinal].Width));
                }
            }
        }

        return peakBytes;
    }

    private static ulong PeakEvaluatorTemporaryOwnedBufferBytes(SimulationIr ir)
    {
        ulong peakBytes = 0;
        foreach (var evaluator in ir.Evaluators)
        {
            if (!IsEvaluatedDuringSettlement(evaluator.Kind))
            {
                continue;
            }

            peakBytes = Math.Max(
                peakBytes,
                EvaluatorTemporaryOwnedBufferBytes(ir, evaluator));
        }

        return peakBytes;
    }

    private static ulong EvaluatorTemporaryOwnedBufferBytes(
        SimulationIr ir,
        SimulationEvaluator evaluator)
    {
        var bytes = OutputPlaneBytes(ir, evaluator);
        return checked(bytes + evaluator.Kind switch
        {
            SimulationEvaluatorKind.MemoryRom
                or SimulationEvaluatorKind.MemoryRamSinglePort => OwnedSlots(
                    checked(
                        (ulong)evaluator.InitialMemory!.Count
                        + ir.Nets[evaluator.InputNetOrdinals[0]].Width)),
            SimulationEvaluatorKind.LogicAnd
                or SimulationEvaluatorKind.LogicNand
                or SimulationEvaluatorKind.LogicOr
                or SimulationEvaluatorKind.LogicNor
                or SimulationEvaluatorKind.LogicXor
                or SimulationEvaluatorKind.LogicXnor => checked(
                    OwnedSlots((ulong)evaluator.InputNetOrdinals.Count)
                    + VectorPlaneBytes(evaluator.Width)),
            SimulationEvaluatorKind.LogicTristate => checked(
                OwnedSlots(2)
                + (2UL * VectorPlaneBytes(evaluator.Width))),
            SimulationEvaluatorKind.LogicMux => MuxTemporaryBytes(ir, evaluator),
            SimulationEvaluatorKind.LogicDemux => checked(
                OwnedSlots(checked(
                    (ulong)evaluator.OutputDriverOrdinals.Count + 2UL))
                + VectorPlaneBytes(ir.Nets[evaluator.InputNetOrdinals[0]].Width)
                + VectorPlaneBytes(ir.Nets[evaluator.InputNetOrdinals[1]].Width)),
            SimulationEvaluatorKind.LogicDecoder => checked(
                OwnedSlots((ulong)evaluator.OutputDriverOrdinals.Count)
                + VectorPlaneBytes(ir.Nets[evaluator.InputNetOrdinals[0]].Width)),
            SimulationEvaluatorKind.LogicPriorityEncoder =>
                PriorityEncoderTemporaryBytes(ir, evaluator),
            SimulationEvaluatorKind.LogicUnsignedCompare => OwnedSlots(2),
            SimulationEvaluatorKind.LogicAdder
                or SimulationEvaluatorKind.LogicSubtractor =>
                OwnedSlots(checked((ulong)evaluator.Width * 2UL)),
            SimulationEvaluatorKind.LogicShift => checked(
                OwnedSlots(ir.Nets[evaluator.InputNetOrdinals[1]].Width)
                + OwnedSlots((ulong)LogicVector.GetWordCount(
                    checked((int)evaluator.Width)))),
            SimulationEvaluatorKind.TopologySplit =>
                VectorPlaneBytes(ir.Nets[evaluator.InputNetOrdinals[0]].Width),
            SimulationEvaluatorKind.TopologyConcat =>
                ConcatTemporaryBytes(ir, evaluator),
            SimulationEvaluatorKind.TopologyZeroExtend
                or SimulationEvaluatorKind.TopologySignExtend => checked(
                    OwnedSlots(checked((ulong)evaluator.Width * 2UL))
                    + VectorPlaneBytes(ir.Nets[evaluator.InputNetOrdinals[0]].Width)),
            SimulationEvaluatorKind.LogicNot
                or SimulationEvaluatorKind.LogicBuffer => 0UL,
            _ => throw new InvalidOperationException(
                "The settlement evaluator kind is undefined."),
        });
    }

    private static ulong MuxTemporaryBytes(
        SimulationIr ir,
        SimulationEvaluator evaluator)
    {
        var dataInputCount = checked(evaluator.InputNetOrdinals.Count - 1);
        var dataWidth = ir.Nets[evaluator.InputNetOrdinals[0]].Width;
        var selectorWidth = ir.Nets[evaluator.InputNetOrdinals[^1]].Width;
        return checked(
            OwnedSlots(checked((ulong)dataInputCount * 2UL))
            + ((ulong)dataInputCount * VectorPlaneBytes(dataWidth))
            + VectorPlaneBytes(selectorWidth));
    }

    private static ulong PriorityEncoderTemporaryBytes(
        SimulationIr ir,
        SimulationEvaluator evaluator)
    {
        var inputCount = (ulong)evaluator.InputNetOrdinals.Count;
        var possibleResultCount = checked(inputCount + 1UL);
        var indexWidth = ir.Drivers[evaluator.OutputDriverOrdinals[0]].Width;
        return checked(
            OwnedSlots(checked(
                inputCount
                + inputCount
                + (possibleResultCount * 4UL)
                + (indexWidth * 2UL)
                + 2UL))
            + (possibleResultCount * VectorPlaneBytes(indexWidth)));
    }

    private static ulong ConcatTemporaryBytes(
        SimulationIr ir,
        SimulationEvaluator evaluator)
    {
        uint widestInput = 0;
        foreach (var netOrdinal in evaluator.InputNetOrdinals)
        {
            widestInput = Math.Max(widestInput, ir.Nets[netOrdinal].Width);
        }

        return checked(
            OwnedSlots((ulong)evaluator.InputNetOrdinals.Count)
            + OwnedSlots(checked((ulong)evaluator.Width * 2UL))
            + VectorPlaneBytes(widestInput));
    }

    private static ulong OutputPlaneBytes(
        SimulationIr ir,
        SimulationEvaluator evaluator)
    {
        ulong bytes = 0;
        foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
        {
            bytes = checked(bytes + VectorPlaneBytes(ir.Drivers[driverOrdinal].Width));
        }

        return bytes;
    }

    private static bool IsEvaluatedDuringSettlement(SimulationEvaluatorKind kind)
    {
        return !SimulationEvaluatorKindFacts.IsSequential(kind)
            && kind is not SimulationEvaluatorKind.InputSource
                and not SimulationEvaluatorKind.ConstantSource
                and not SimulationEvaluatorKind.ClockSource
                and not SimulationEvaluatorKind.OutputSink;
    }

    private static ulong VectorPlaneBytes(uint width)
    {
        return checked(
            (ulong)LogicVector.GetWordCount(checked((int)width))
            * 2UL
            * sizeof(ulong));
    }

    private static ulong NetResolutionPlaneBytes(uint width)
    {
        return checked(
            (ulong)LogicVector.GetWordCount(checked((int)width))
            * 5UL
            * sizeof(ulong));
    }

    private static ulong OwnedSlots(ulong count)
    {
        return checked(count * sizeof(ulong));
    }
}
