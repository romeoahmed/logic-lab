using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal static class HotSwapOwnedBufferAccounting
{
    private const ulong ScheduledBatchSlotCount = 3;
    private const ulong TimeIndexSlotCount = 2;
    private const ulong AssignmentIndexSlotCount = 2;

    public static HotSwapOwnedBufferEstimate MeasureCandidatePeak(
        SimulationSessionState state,
        CompilationArtifact replacement,
        ulong migratedRamCellReferenceCount,
        int preservedProbeCount)
    {
        try
        {
            return new HotSwapOwnedBufferEstimate(
                checked(
                    CommittedWorkingLayerBytes(state)
                    + ReplacementCandidateBytes(
                        replacement.SimulationIr,
                        state.LogicalTime,
                        migratedRamCellReferenceCount,
                        preservedProbeCount)),
                IsSaturated: false);
        }
        catch (OverflowException)
        {
            return new HotSwapOwnedBufferEstimate(
                ulong.MaxValue,
                IsSaturated: true);
        }
    }

    public static HotSwapOwnedBufferEstimate AddTraceFork(
        HotSwapOwnedBufferEstimate candidatePeak,
        SimulationTraceStore trace,
        IReadOnlyList<(ProbeState Probe, LogicVector Value)> observations)
    {
        if (candidatePeak.IsSaturated)
        {
            return candidatePeak;
        }

        try
        {
            return new HotSwapOwnedBufferEstimate(
                checked(
                    candidatePeak.Bytes
                    + trace.ForkCandidateOwnedBufferBytes(observations)),
                IsSaturated: false);
        }
        catch (OverflowException)
        {
            return new HotSwapOwnedBufferEstimate(
                ulong.MaxValue,
                IsSaturated: true);
        }
    }

    private static ulong CommittedWorkingLayerBytes(SimulationSessionState state)
    {
        var bytes = checked(
            ReferenceSlots(state.DriverValues.Length)
            + ReferenceSlots(state.NetValues.Length)
            + ReferenceSlots(state.SequentialStates.Length)
            + ReferenceSlots(state.MemoryStates.Length)
            + ReferenceSlots(state.Probes.Length)
            + VectorPlaneBytes(state.DriverValues)
            + VectorPlaneBytes(state.NetValues)
            + VectorPlaneBytes(state.SequentialStates)
            + ScheduledEventFrontierBytes(state)
            + state.ClockEvents.RetainedOwnedBufferBytes);
        foreach (var memory in state.MemoryStates)
        {
            if (memory is not null)
            {
                bytes = checked(
                    bytes
                    + ReferenceSlots(memory.Length)
                    + VectorPlaneBytes(memory));
            }
        }

        return checked(bytes + state.Trace.RetainedOwnedBufferBytes);
    }

    private static ulong ReplacementCandidateBytes(
        SimulationIr replacement,
        ulong logicalTimeOrigin,
        ulong migratedRamCellReferenceCount,
        int preservedProbeCount)
    {
        var evaluatorCount = replacement.Evaluators.Count;
        var driverCount = replacement.Drivers.Count;
        var netCount = replacement.Nets.Count;
        var bytes = checked(
            ReferenceSlots(evaluatorCount)
            + ReferenceSlots(evaluatorCount)
            + ReferenceSlots(driverCount)
            + ReferenceSlots(netCount)
            + ReferenceSlots(netCount)
            + ReferenceSlots(preservedProbeCount)
            + ReferenceSlots(preservedProbeCount)
            + ClockEventCalendar.CandidateOwnedBufferBytes(
                replacement,
                logicalTimeOrigin));
        for (var index = 0; index < replacement.Drivers.Count; index++)
        {
            bytes = checked(
                bytes + VectorPlaneBytes(replacement.Drivers[index].Width));
        }

        for (var index = 0; index < replacement.Nets.Count; index++)
        {
            var net = replacement.Nets[index];
            bytes = checked(
                bytes
                + VectorPlaneBytes(net.Width)
                + NetResolutionCausePlaneBytes(net.Width));
        }

        for (var index = 0; index < replacement.Evaluators.Count; index++)
        {
            var evaluator = replacement.Evaluators[index];
            if (evaluator.InitialMemory is not { } memory)
            {
                continue;
            }

            bytes = checked(bytes + ReferenceSlots(memory.Count));
        }

        return checked(bytes + ReferenceSlots(migratedRamCellReferenceCount));
    }

    private static ulong ScheduledEventFrontierBytes(SimulationSessionState state)
    {
        ulong bytes = 0;
        foreach (var item in state.ScheduledBatches.UnorderedItems)
        {
            bytes = checked(
                bytes
                + OwnedSlots(ScheduledBatchSlotCount)
                + ReferenceSlots(item.Element.Assignments.Length));
        }

        foreach (var bucket in state.ScheduledAssignmentsByTime)
        {
            bytes = checked(
                bytes
                + OwnedSlots(TimeIndexSlotCount)
                + OwnedSlots(checked(
                    (ulong)bucket.Value.Count * AssignmentIndexSlotCount)));
        }

        return bytes;
    }

    private static ulong VectorPlaneBytes(uint width)
    {
        return checked(
            (ulong)LogicVector.GetWordCount(checked((int)width))
            * 2UL
            * sizeof(ulong));
    }

    private static ulong NetResolutionCausePlaneBytes(uint width)
    {
        return checked(
            (ulong)LogicVector.GetWordCount(checked((int)width))
            * 3UL
            * sizeof(ulong));
    }

    private static ulong VectorPlaneBytes(LogicVector?[] vectors)
    {
        ulong bytes = 0;
        foreach (var vector in vectors)
        {
            if (vector is not null)
            {
                bytes = checked(
                    bytes
                    + ((ulong)vector.WordCount * 2UL * sizeof(ulong)));
            }
        }

        return bytes;
    }

    private static ulong ReferenceSlots(int count)
    {
        return ReferenceSlots((ulong)count);
    }

    private static ulong ReferenceSlots(ulong count)
    {
        return OwnedSlots(count);
    }

    private static ulong OwnedSlots(ulong count)
    {
        return checked(count * sizeof(ulong));
    }
}

internal readonly record struct HotSwapOwnedBufferEstimate(
    ulong Bytes,
    bool IsSaturated);
