using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal static class HotSwapOwnedBufferAccounting
{
    public static HotSwapOwnedBufferEstimate MeasurePeak(
        SimulationSessionState state,
        CompilationArtifact replacement,
        HotSwapStateMigration[] migrations,
        int preservedProbeCount)
    {
        try
        {
            return new HotSwapOwnedBufferEstimate(
                checked(
                    CommittedWorkingLayerBytes(state)
                    + ReplacementCandidateBytes(
                        state,
                        replacement.SimulationIr,
                        migrations,
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
            + VectorPlaneBytes(state.SequentialStates));
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
        SimulationSessionState state,
        SimulationIr replacement,
        HotSwapStateMigration[] migrations,
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
            + ReferenceSlots(preservedProbeCount));
        for (var index = 0; index < replacement.Drivers.Count; index++)
        {
            bytes = checked(
                bytes + VectorPlaneBytes(replacement.Drivers[index].Width));
        }

        var maximumNetWordCount = 0;
        for (var index = 0; index < replacement.Nets.Count; index++)
        {
            var net = replacement.Nets[index];
            bytes = checked(
                bytes
                + VectorPlaneBytes(net.Width)
                + NetResolutionCausePlaneBytes(net.Width));
            maximumNetWordCount = Math.Max(
                maximumNetWordCount,
                LogicVector.GetWordCount(checked((int)net.Width)));
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

        foreach (var migration in migrations)
        {
            if (migration.Current.Kind == SimulationEvaluatorKind.MemoryRamSinglePort)
            {
                bytes = checked(
                    bytes + ReferenceSlots(migration.Replacement.InitialMemory!.Count));
            }
        }

        return checked(
            bytes
            + state.Trace.ForkCandidateOwnedBufferBytes(
                preservedProbeCount,
                maximumNetWordCount));
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
        return checked((ulong)count * sizeof(ulong));
    }
}

internal readonly record struct HotSwapOwnedBufferEstimate(
    ulong Bytes,
    bool IsSaturated);
