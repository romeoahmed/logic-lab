using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    private static SimulationCommandOutcome HotSwap(
        SimulationSessionState state,
        CompilationArtifact replacement,
        ulong maximumPeakOwnedBufferBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CompilationArtifactValidator.Validate(
            replacement.SimulationIr,
            replacement.SourceMap,
            cancellationToken);
        var current = state.Artifact!;
        var migrationPreflight = InspectStateMigrations(
            current,
            replacement,
            cancellationToken);
        var probePreflight = InspectProbeBindings(
            state.Probes,
            replacement.SourceMap,
            cancellationToken);
        if (migrationPreflight.IncompatibleCount != 0)
        {
            return new HotSwapIncompatible(
                state.SessionVersion,
                current.Key,
                FindIncompatibleStateSources(
                    current,
                    replacement,
                    migrationPreflight.IncompatibleCount,
                    cancellationToken),
                FindUnresolvedProbeIds(
                    state.Probes,
                    replacement.SourceMap,
                    state.Probes.Length - probePreflight.PreservedProbeCount,
                    cancellationToken));
        }

        var candidatePeakOwnedBuffers = HotSwapOwnedBufferAccounting.MeasureCandidatePeak(
            state,
            replacement,
            migrationPreflight.MigratedRamCellReferenceCount,
            probePreflight.PreservedProbeCount,
            state.Probes.Length - probePreflight.PreservedProbeCount);
        if (candidatePeakOwnedBuffers.IsSaturated
            || candidatePeakOwnedBuffers.Bytes > maximumPeakOwnedBufferBytes)
        {
            return new HotSwapResourceLimitExceeded(
                state.SessionVersion,
                current.Key,
                maximumPeakOwnedBufferBytes,
                candidatePeakOwnedBuffers.Bytes);
        }

        EnsureWorkingLayerFits(replacement.SimulationIr, state.SimulationPolicy);
        var migrations = CreateStateMigrations(
            current,
            replacement,
            migrationPreflight.MigrationCount,
            cancellationToken);
        var probeBindings = RebindProbes(
            state.Probes,
            replacement.SourceMap,
            probePreflight.PreservedProbeCount,
            cancellationToken);
        var replacementIr = replacement.SimulationIr;
        var sequentialStates = CreateSequentialStates(replacementIr);
        var memoryStates = CreateMemoryStates(replacementIr);
        foreach (var migration in migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SimulationEvaluatorKindFacts.IsSequential(migration.Current.Kind))
            {
                sequentialStates[migration.Replacement.Ordinal] =
                    state.SequentialStates[migration.Current.Ordinal];
            }
            else
            {
                memoryStates[migration.Replacement.Ordinal] =
                    (LogicVector[])state.MemoryStates[migration.Current.Ordinal]!.Clone();
            }
        }

        var driverValues = CreateDriverValues(replacementIr, sequentialStates);
        var settlement = SettleCombinational(
            replacement,
            driverValues,
            memoryStates,
            state.SimulationPolicy,
            new SettlementWork(),
            cancellationToken);
        var probes = probeBindings.Probes;
        var changedProbeSummary = MeasureChangedProbeObservations(
            state,
            probes,
            settlement.NetValues);
        var diagnosticBuffers = SimulationNetDiagnostics.MeasureOwnedBuffers(
            replacement,
            driverValues,
            settlement.NetResolutions);
        var peakOwnedBuffers = HotSwapOwnedBufferAccounting.AddPublicationAndTraceFork(
            candidatePeakOwnedBuffers,
            state.Trace,
            changedProbeSummary,
            diagnosticBuffers.OwnedReferenceSlotCount,
            migrationPreflight.MigrationCount,
            probePreflight.PreservedProbeCount);
        if (peakOwnedBuffers.IsSaturated
            || peakOwnedBuffers.Bytes > maximumPeakOwnedBufferBytes)
        {
            return new HotSwapResourceLimitExceeded(
                state.SessionVersion,
                current.Key,
                maximumPeakOwnedBufferBytes,
                peakOwnedBuffers.Bytes);
        }

        var changedProbeObservations = CreateChangedProbeObservations(
            state,
            probes,
            settlement.NetValues,
            changedProbeSummary.Count);
        var diagnostics = SimulationNetDiagnostics.CreateExact(
            replacement,
            driverValues,
            settlement.NetResolutions,
            diagnosticBuffers.DiagnosticCount);
        var observedProbes = probes.Select(probe => new ProbeObservation(
            probe.ProbeId,
            probe.Source,
            settlement.NetValues[probe.NetOrdinal])).ToArray();
        var trace = state.Trace.ForkWithAppend(
            state.LogicalTime,
            changedProbeObservations);
        var clockEvents = CreateClockEventCalendar(replacementIr, state.LogicalTime);
        var sessionVersion = checked(state.SessionVersion + 1UL);
        var scheduledBatches =
            new PriorityQueue<ScheduledStimulusBatch, ScheduledStimulusPriority>();
        var scheduledAssignmentsByTime =
            new Dictionary<ulong, SortedDictionary<int, LogicVector>>();
        var migratedStateSources = new CompilationSource[migrations.Length];
        for (var index = 0; index < migrations.Length; index++)
        {
            migratedStateSources[index] = migrations[index].Source;
        }

        Array.Sort(migratedStateSources, CompilationSourceComparer.Instance);
        var preservedProbeIds = probes.Select(probe => probe.ProbeId).ToArray();
        var committed = new HotSwapCommitted(
            sessionVersion,
            replacement.Key,
            new HotSwapMigrationEvidence(
                migratedStateSources,
                preservedProbeIds,
                probeBindings.UnresolvedProbeIds),
            preservedProbeIds,
            observedProbes,
            diagnostics,
            trace.Cursor);
        cancellationToken.ThrowIfCancellationRequested();

        state.Artifact = replacement;
        state.DriverValues = driverValues;
        state.NetValues = settlement.NetValues;
        state.SequentialStates = sequentialStates;
        state.MemoryStates = memoryStates;
        state.Probes = probes;
        state.Trace = trace;
        state.Diagnostics = diagnostics;
        state.SessionVersion = sessionVersion;
        state.NextStimulusSequence = 0;
        state.ScheduledBatches = scheduledBatches;
        state.ScheduledAssignmentsByTime = scheduledAssignmentsByTime;
        state.ScheduledAssignmentCount = 0;
        state.ClockEvents = clockEvents;
        return committed;
    }

    private static void EnsureWorkingLayerFits(
        SimulationIr ir,
        SimulationPolicy policy)
    {
        var observed = checked(
            (ulong)ir.Drivers.Count
            + (ulong)ir.Nets.Count
            + (ulong)ir.Evaluators.Count(evaluator =>
                SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind))
            + (ulong)ir.Evaluators.Count(evaluator =>
                evaluator.Kind == SimulationEvaluatorKind.ClockSource)
            + ir.Evaluators.Aggregate(
                0UL,
                (count, evaluator) => checked(
                    count + (ulong)(evaluator.InitialMemory?.Count ?? 0))));
        if (observed > policy.Maximum(SimulationDimension.WorkingLayerSlotCount))
        {
            throw new SimulationPolicyLimitException(
                SimulationDimension.WorkingLayerSlotCount,
                observed);
        }
    }

    private static ChangedProbeBufferMeasure MeasureChangedProbeObservations(
        SimulationSessionState state,
        ProbeState[] reboundProbes,
        LogicVector[] replacementNetValues)
    {
        var count = 0;
        ulong packedWordCount = 0;
        var reboundIndex = 0;
        foreach (var currentProbe in state.Probes)
        {
            if (reboundIndex == reboundProbes.Length)
            {
                break;
            }

            var reboundProbe = reboundProbes[reboundIndex];
            if (reboundProbe.ProbeId != currentProbe.ProbeId)
            {
                continue;
            }

            var replacementValue = replacementNetValues[reboundProbe.NetOrdinal];
            if (!ValuesEqual(
                    state.NetValues[currentProbe.NetOrdinal],
                    replacementValue))
            {
                count++;
                packedWordCount = checked(
                    packedWordCount + (ulong)replacementValue.WordCount);
            }

            reboundIndex++;
        }

        return new ChangedProbeBufferMeasure(count, packedWordCount);
    }

    private static (ProbeState Probe, LogicVector Value)[] CreateChangedProbeObservations(
        SimulationSessionState state,
        ProbeState[] reboundProbes,
        LogicVector[] replacementNetValues,
        int observationCount)
    {
        var observations = new (ProbeState Probe, LogicVector Value)[observationCount];
        var observationIndex = 0;
        var reboundIndex = 0;
        foreach (var currentProbe in state.Probes)
        {
            if (reboundIndex == reboundProbes.Length)
            {
                break;
            }

            var reboundProbe = reboundProbes[reboundIndex];
            if (reboundProbe.ProbeId != currentProbe.ProbeId)
            {
                continue;
            }

            var replacementValue = replacementNetValues[reboundProbe.NetOrdinal];
            if (!ValuesEqual(
                    state.NetValues[currentProbe.NetOrdinal],
                    replacementValue))
            {
                observations[observationIndex++] = (reboundProbe, replacementValue);
            }

            reboundIndex++;
        }

        return observations;
    }
}
