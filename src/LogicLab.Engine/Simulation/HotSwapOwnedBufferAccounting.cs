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
        ulong migratedRamOwnedBufferBytes,
        int preservedProbeCount,
        int unresolvedProbeCount,
        HotSwapConsumerBufferRequirements consumerBuffers)
    {
        try
        {
            return new HotSwapOwnedBufferEstimate(
                checked(
                    consumerBuffers.RetainedOwnedBufferBytes
                    + CommittedWorkingLayerBytes(state)
                    + ReplacementCandidateBytes(
                        replacement.SimulationIr,
                        state.LogicalTime,
                        migratedRamOwnedBufferBytes,
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
        ChangedProbeBufferMeasure observations,
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
                    + trace.ForkCandidateOwnedBufferBytes(
                        observations.Count,
                        observations.PackedWordCount)
                    + ReferenceSlots(checked((ulong)observations.Count * 2UL))
                    + PreCommitPublicationOwnedBufferBytes(
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

    public static HotSwapOwnedBufferEstimate MeasurePostCommitPeak(
        CompilationArtifact replacement,
        LogicVector[] driverValues,
        LogicVector[] netValues,
        LogicVector?[] sequentialStates,
        PackedMemory?[] memoryStates,
        ProbeState[] probes,
        SimulationTraceStore currentTrace,
        ChangedProbeBufferMeasure changedProbes,
        ulong diagnosticOwnedReferenceSlotCount,
        int migrationCount,
        int unresolvedProbeCount,
        ulong logicalTimeOrigin,
        HotSwapConsumerBufferRequirements consumerBuffers,
        ObservedProbeBufferMeasure observedProbes)
    {
        try
        {
            return new HotSwapOwnedBufferEstimate(
                checked(
                    RetainedReplacementWorkingLayerBytes(
                        replacement,
                        driverValues,
                        netValues,
                        sequentialStates,
                        memoryStates,
                        probes,
                        currentTrace,
                        changedProbes,
                        diagnosticOwnedReferenceSlotCount,
                        logicalTimeOrigin)
                    + PostCommitOutcomeOwnedBufferBytes(
                        migrationCount,
                        observedProbes.Count,
                        unresolvedProbeCount)
                    + consumerBuffers.RetainedOwnedBufferBytes
                    + ConsumerPublicationOwnedBufferBytes(
                        consumerBuffers,
                        observedProbes)),
                IsSaturated: false);
        }
        catch (OverflowException)
        {
            return new HotSwapOwnedBufferEstimate(
                ulong.MaxValue,
                IsSaturated: true);
        }
    }

    public static HotSwapOwnedBufferEstimate Maximum(
        HotSwapOwnedBufferEstimate left,
        HotSwapOwnedBufferEstimate right)
    {
        if (left.IsSaturated || right.IsSaturated)
        {
            return new HotSwapOwnedBufferEstimate(ulong.MaxValue, IsSaturated: true);
        }

        return new HotSwapOwnedBufferEstimate(
            Math.Max(left.Bytes, right.Bytes),
            IsSaturated: false);
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
        return checked(bytes + state.Trace.RetainedOwnedBufferBytes);
    }

    private static ulong RetainedReplacementWorkingLayerBytes(
        CompilationArtifact replacement,
        LogicVector[] driverValues,
        LogicVector[] netValues,
        LogicVector?[] sequentialStates,
        PackedMemory?[] memoryStates,
        ProbeState[] probes,
        SimulationTraceStore currentTrace,
        ChangedProbeBufferMeasure changedProbes,
        ulong diagnosticOwnedReferenceSlotCount,
        ulong logicalTimeOrigin)
    {
        var bytes = checked(
            ReferenceSlots(driverValues.Length)
            + ReferenceSlots(netValues.Length)
            + ReferenceSlots(sequentialStates.Length)
            + ReferenceSlots(memoryStates.Length)
            + ReferenceSlots(probes.Length)
            + ReferenceSlots(diagnosticOwnedReferenceSlotCount)
            + OwnedVectorPlaneBytes(
                replacement,
                driverValues,
                netValues,
                sequentialStates,
                memoryStates)
            + ClockEventCalendar.CandidateOwnedBufferBytes(
                replacement.SimulationIr,
                logicalTimeOrigin)
            + currentTrace.ForkResultRetainedOwnedBufferBytes(
                changedProbes.Count,
                changedProbes.PackedWordCount));
        return bytes;
    }

    private static ulong ReplacementCandidateBytes(
        SimulationIr replacement,
        ulong logicalTimeOrigin,
        ulong migratedRamOwnedBufferBytes,
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
            + SettlementOwnedBufferAccounting.PeakOwnedBufferBytes(replacement)
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

        return checked(bytes + migratedRamOwnedBufferBytes);
    }

    private static ulong PreCommitPublicationOwnedBufferBytes(
        ulong diagnosticOwnedReferenceSlotCount,
        int migrationCount,
        int preservedProbeCount)
    {
        return checked(
            ReferenceSlots(diagnosticOwnedReferenceSlotCount)
            + ReferenceSlots(migrationCount)
            + ReferenceSlots(checked((ulong)preservedProbeCount * 2UL)));
    }

    private static ulong PostCommitOutcomeOwnedBufferBytes(
        int migrationCount,
        int observedProbeCount,
        int unresolvedProbeCount)
    {
        return ReferenceSlots(checked(
            (ulong)migrationCount
            + ((ulong)observedProbeCount * 2UL)
            + (ulong)unresolvedProbeCount));
    }

    private static ulong ConsumerPublicationOwnedBufferBytes(
        HotSwapConsumerBufferRequirements consumerBuffers,
        ObservedProbeBufferMeasure observedProbes)
    {
        return checked(
            ReferenceSlots(
                (ulong)observedProbes.Count
                * consumerBuffers.OwnedReferenceSlotsPerObservedProbe)
            + (observedProbes.BitCount
                * consumerBuffers.OwnedBytesPerObservedProbeBit));
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
        return OwnedVectorPlaneBytes(
            state.Artifact!,
            state.DriverValues,
            state.NetValues,
            state.SequentialStates,
            state.MemoryStates);
    }

    private static ulong OwnedVectorPlaneBytes(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        LogicVector[] netValues,
        LogicVector?[] sequentialStates,
        PackedMemory?[] memoryStates)
    {
        var sharedArtifactVectors = new HashSet<LogicVector>(
            ReferenceEqualityComparer.Instance);
        foreach (var evaluator in artifact.SimulationIr.Evaluators)
        {
            if (evaluator.InitialValue is { } initialValue)
            {
                _ = sharedArtifactVectors.Add(initialValue);
            }

        }

        var ownedVectors = new HashSet<LogicVector>(ReferenceEqualityComparer.Instance);
        AddOwnedVectors(driverValues, sharedArtifactVectors, ownedVectors);
        AddOwnedVectors(netValues, sharedArtifactVectors, ownedVectors);
        AddOwnedVectors(sequentialStates, sharedArtifactVectors, ownedVectors);
        ulong memoryBytes = 0;
        for (var index = 0; index < memoryStates.Length; index++)
        {
            var memory = memoryStates[index];
            if (memory is not null
                && !ReferenceEquals(
                    memory,
                    artifact.SimulationIr.Evaluators[index].InitialMemory))
            {
                memoryBytes = checked(memoryBytes + memory.OwnedBufferBytes);
            }
        }

        return checked(memoryBytes + ownedVectors.Aggregate(
            0UL,
            (bytes, vector) => checked(
                bytes + ((ulong)vector.WordCount * 2UL * sizeof(ulong)))));
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
            bytes = checked(
                bytes + ReplacementOwnedOutputPlaneBytes(replacement, evaluator));
        }

        return bytes;
    }

    private static ulong ReplacementOwnedOutputPlaneBytes(
        SimulationIr replacement,
        SimulationEvaluator evaluator)
    {
        if (evaluator.Kind is SimulationEvaluatorKind.LogicDemux)
        {
            var outputWidth = replacement.Drivers[
                evaluator.OutputDriverOrdinals[0]].Width;
            return checked(2UL * VectorPlaneBytes(outputWidth));
        }

        ulong bytes = 0;
        for (var index = 0; index < evaluator.OutputDriverOrdinals.Count; index++)
        {
            if (SharesArtifactOrStateVector(evaluator, index))
            {
                continue;
            }

            var driver = replacement.Drivers[evaluator.OutputDriverOrdinals[index]];
            bytes = checked(bytes + VectorPlaneBytes(driver.Width));
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
        foreach (var (batch, _) in state.ScheduledBatches.UnorderedItems)
        {
            bytes = checked(
                bytes
                + OwnedSlots(ScheduledBatchSlotCount)
                + ReferenceSlots(batch.Assignments.Length));
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

internal readonly record struct ChangedProbeBufferMeasure(
    int Count,
    ulong PackedWordCount);

internal readonly record struct ObservedProbeBufferMeasure(
    int Count,
    ulong BitCount);
