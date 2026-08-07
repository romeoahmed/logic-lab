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
        int preservedProbeCount,
        int unresolvedProbeCount)
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
                        preservedProbeCount,
                        unresolvedProbeCount)),
                IsSaturated: false);
        }
        catch (OverflowException)
        {
            return new HotSwapOwnedBufferEstimate(
                ulong.MaxValue,
                IsSaturated: true);
        }
    }

    public static HotSwapOwnedBufferEstimate AddPublicationAndTraceFork(
        HotSwapOwnedBufferEstimate candidatePeak,
        SimulationTraceStore trace,
        IReadOnlyList<(ProbeState Probe, LogicVector Value)> observations,
        int diagnosticCount,
        ulong diagnosticOwnedReferenceSlotCount,
        int migrationCount,
        int preservedProbeCount)
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
                    + trace.ForkCandidateOwnedBufferBytes(observations)
                    + PublicationOwnedBufferBytes(
                        diagnosticCount,
                        diagnosticOwnedReferenceSlotCount,
                        migrationCount,
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
            + DiagnosticOwnedBufferBytes(state.Diagnostics)
            + OwnedVectorPlaneBytes(state)
            + ScheduledEventFrontierBytes(state)
            + state.ClockEvents.RetainedOwnedBufferBytes);
        foreach (var memory in state.MemoryStates)
        {
            if (memory is not null)
            {
                bytes = checked(
                    bytes
                    + ReferenceSlots(memory.Length));
            }
        }

        return checked(bytes + state.Trace.RetainedOwnedBufferBytes);
    }

    private static ulong ReplacementCandidateBytes(
        SimulationIr replacement,
        ulong logicalTimeOrigin,
        ulong migratedRamCellReferenceCount,
        int preservedProbeCount,
        int unresolvedProbeCount)
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
            + ReferenceSlots(unresolvedProbeCount)
            + ClockEventCalendar.CandidateOwnedBufferBytes(
                replacement,
                logicalTimeOrigin));
        bytes = checked(bytes + ReplacementOwnedDriverPlaneBytes(replacement));

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

    private static ulong PublicationOwnedBufferBytes(
        int diagnosticCount,
        ulong diagnosticOwnedReferenceSlotCount,
        int migrationCount,
        int preservedProbeCount)
    {
        return checked(
            ReferenceSlots(
                diagnosticOwnedReferenceSlotCount + (ulong)diagnosticCount)
            + ReferenceSlots(migrationCount)
            + ReferenceSlots(checked((ulong)preservedProbeCount * 3UL)));
    }

    private static ulong DiagnosticOwnedBufferBytes(
        SimulationDiagnostic[] diagnostics)
    {
        var referenceSlots = (ulong)diagnostics.Length;
        foreach (var diagnostic in diagnostics)
        {
            referenceSlots = checked(
                referenceSlots
                + (ulong)diagnostic.Arguments.Count
                + (ulong)diagnostic.Related.Count);
        }

        return ReferenceSlots(referenceSlots);
    }

    private static ulong OwnedVectorPlaneBytes(SimulationSessionState state)
    {
        var sharedArtifactVectors = new HashSet<LogicVector>(
            ReferenceEqualityComparer.Instance);
        foreach (var evaluator in state.Artifact!.SimulationIr.Evaluators)
        {
            if (evaluator.InitialValue is { } initialValue)
            {
                _ = sharedArtifactVectors.Add(initialValue);
            }

            if (evaluator.InitialMemory is { } initialMemory)
            {
                sharedArtifactVectors.UnionWith(initialMemory);
            }
        }

        var ownedVectors = new HashSet<LogicVector>(ReferenceEqualityComparer.Instance);
        AddOwnedVectors(state.DriverValues, sharedArtifactVectors, ownedVectors);
        AddOwnedVectors(state.NetValues, sharedArtifactVectors, ownedVectors);
        AddOwnedVectors(state.SequentialStates, sharedArtifactVectors, ownedVectors);
        foreach (var memory in state.MemoryStates)
        {
            if (memory is not null)
            {
                AddOwnedVectors(memory, sharedArtifactVectors, ownedVectors);
            }
        }

        return ownedVectors.Aggregate(
            0UL,
            (bytes, vector) => checked(
                bytes + ((ulong)vector.WordCount * 2UL * sizeof(ulong))));
    }

    private static void AddOwnedVectors(
        IEnumerable<LogicVector?> vectors,
        HashSet<LogicVector> sharedArtifactVectors,
        HashSet<LogicVector> ownedVectors)
    {
        foreach (var vector in vectors)
        {
            if (vector is not null && !sharedArtifactVectors.Contains(vector))
            {
                _ = ownedVectors.Add(vector);
            }
        }
    }

    private static ulong ReplacementOwnedDriverPlaneBytes(SimulationIr replacement)
    {
        ulong bytes = 0;
        foreach (var evaluator in replacement.Evaluators)
        {
            for (var index = 0; index < evaluator.OutputDriverOrdinals.Count; index++)
            {
                if (SharesArtifactOrStateVector(evaluator, index))
                {
                    continue;
                }

                var driver = replacement.Drivers[
                    evaluator.OutputDriverOrdinals[index]];
                bytes = checked(bytes + VectorPlaneBytes(driver.Width));
            }
        }

        return bytes;
    }

    private static bool SharesArtifactOrStateVector(
        SimulationEvaluator evaluator,
        int outputIndex)
    {
        return evaluator.Kind is SimulationEvaluatorKind.InputSource
            or SimulationEvaluatorKind.ConstantSource
            or SimulationEvaluatorKind.ClockSource
            || (outputIndex == 0
                && SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind));
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
