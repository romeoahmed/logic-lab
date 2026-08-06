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
        var replacementSources = ReplacementSourceIndex.Create(
            replacement,
            cancellationToken);
        var probeBindings = RebindProbes(
            state.Probes,
            replacementSources);
        var migrations = FindStateMigrations(
            current,
            replacementSources,
            cancellationToken);
        if (migrations.IncompatibleSources.Length != 0)
        {
            return new HotSwapIncompatible(
                state.SessionVersion,
                current.Key,
                migrations.IncompatibleSources,
                probeBindings.UnresolvedProbeIds);
        }

        var candidatePeakOwnedBuffers = HotSwapOwnedBufferAccounting.MeasureCandidatePeak(
            state,
            replacement,
            migrations.Items,
            probeBindings.Probes.Length);
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
        var replacementIr = replacement.SimulationIr;
        var sequentialStates = CreateSequentialStates(replacementIr);
        var memoryStates = CreateMemoryStates(replacementIr);
        foreach (var migration in migrations.Items)
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
        var diagnostics = SimulationNetDiagnostics.Create(
            replacement,
            driverValues,
            settlement.NetResolutions);
        var probes = probeBindings.Probes;
        var changedProbeObservations = ChangedProbeObservations(
            state,
            probes,
            settlement.NetValues);
        var peakOwnedBuffers = HotSwapOwnedBufferAccounting.AddTraceFork(
            candidatePeakOwnedBuffers,
            state.Trace,
            changedProbeObservations);
        if (peakOwnedBuffers.IsSaturated
            || peakOwnedBuffers.Bytes > maximumPeakOwnedBufferBytes)
        {
            return new HotSwapResourceLimitExceeded(
                state.SessionVersion,
                current.Key,
                maximumPeakOwnedBufferBytes,
                peakOwnedBuffers.Bytes);
        }

        var observedProbes = probes.Select(probe => new ProbeObservation(
            probe.ProbeId,
            probe.Source,
            settlement.NetValues[probe.NetOrdinal])).ToArray();
        var trace = state.Trace.Fork();
        trace.Append(
            state.LogicalTime,
            changedProbeObservations);
        var clockEvents = CreateClockEventCalendar(replacementIr, state.LogicalTime);
        var sessionVersion = checked(state.SessionVersion + 1UL);
        var scheduledBatches =
            new PriorityQueue<ScheduledStimulusBatch, ScheduledStimulusPriority>();
        var scheduledAssignmentsByTime =
            new Dictionary<ulong, SortedDictionary<int, LogicVector>>();
        var preservedProbeIds = probes.Select(probe => probe.ProbeId).ToArray();
        var committed = new HotSwapCommitted(
            sessionVersion,
            replacement.Key,
            new HotSwapMigrationEvidence(
                [.. migrations.Items
                    .Select(item => item.Source)
                    .Order(CompilationSourceComparer.Instance)],
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

    private static StateMigrationResult FindStateMigrations(
        CompilationArtifact current,
        ReplacementSourceIndex replacementSources,
        CancellationToken cancellationToken)
    {
        var migrations = new List<HotSwapStateMigration>();
        var incompatible = new List<CompilationSource>();
        foreach (var evaluator in current.SimulationIr.Evaluators.Where(IsMigratedState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = current.SourceMap.Evaluators[evaluator.Ordinal].Source;
            if (!replacementSources.TryGetEvaluator(source, out var replacementEvaluator)
                || !HasCompatibleStateSchema(evaluator, replacementEvaluator))
            {
                incompatible.Add(source);
                continue;
            }

            migrations.Add(new HotSwapStateMigration(
                evaluator,
                replacementEvaluator,
                source));
        }

        return new StateMigrationResult(
            [.. migrations],
            [.. incompatible.Order(CompilationSourceComparer.Instance)]);
    }

    private static bool IsMigratedState(SimulationEvaluator evaluator)
    {
        return SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind)
            || evaluator.Kind == SimulationEvaluatorKind.MemoryRamSinglePort;
    }

    private static bool HasCompatibleStateSchema(
        SimulationEvaluator current,
        SimulationEvaluator replacement)
    {
        if (current.Kind != replacement.Kind
            || current.ContractKey != replacement.ContractKey
            || current.Width != replacement.Width)
        {
            return false;
        }

        if (current.Kind != SimulationEvaluatorKind.MemoryRamSinglePort)
        {
            return current.InitialValue?.Width == replacement.InitialValue?.Width;
        }

        var currentMemory = current.InitialMemory!;
        var replacementMemory = replacement.InitialMemory!;
        return currentMemory.Count == replacementMemory.Count
            && currentMemory.Zip(replacementMemory).All(pair =>
                pair.First.Width == pair.Second.Width);
    }

    private static ProbeBindingResult RebindProbes(
        ProbeState[] probes,
        ReplacementSourceIndex replacementSources)
    {
        var rebound = new List<ProbeState>();
        var unresolved = new List<ProbeId>();
        var boundNetOrdinals = new HashSet<int>();
        foreach (var probe in probes)
        {
            if (!replacementSources.TryGetNetOrdinal(probe.Source, out var netOrdinal)
                || !boundNetOrdinals.Add(netOrdinal))
            {
                unresolved.Add(probe.ProbeId);
                continue;
            }

            rebound.Add(new ProbeState(probe.ProbeId, probe.Source, netOrdinal));
        }

        return new ProbeBindingResult([.. rebound], [.. unresolved]);
    }

    private static (ProbeState Probe, LogicVector Value)[] ChangedProbeObservations(
        SimulationSessionState state,
        ProbeState[] reboundProbes,
        LogicVector[] replacementNetValues)
    {
        var observations = new List<(ProbeState Probe, LogicVector Value)>(
            reboundProbes.Length);
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
                observations.Add((reboundProbe, replacementValue));
            }

            reboundIndex++;
        }

        return [.. observations];
    }

    private sealed record StateMigrationResult(
        HotSwapStateMigration[] Items,
        CompilationSource[] IncompatibleSources);

    private sealed record ProbeBindingResult(
        ProbeState[] Probes,
        ProbeId[] UnresolvedProbeIds);

    private sealed class ReplacementSourceIndex
    {
        private readonly Dictionary<CompilationSource, SimulationEvaluator> evaluators = new(
            CompilationSourceEqualityComparer.Instance);
        private readonly Dictionary<CompilationSource, int> nets = new(
            CompilationSourceEqualityComparer.Instance);

        private ReplacementSourceIndex()
        {
        }

        public static ReplacementSourceIndex Create(
            CompilationArtifact artifact,
            CancellationToken cancellationToken)
        {
            var index = new ReplacementSourceIndex();
            foreach (var entry in artifact.SourceMap.Evaluators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index.evaluators.TryAdd(
                    entry.Source,
                    artifact.SimulationIr.Evaluators[entry.Ordinal]);
            }

            foreach (var entry in artifact.SourceMap.Nets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index.nets.TryAdd(entry.Source, entry.Ordinal);
            }

            foreach (var entry in artifact.SourceMap.NetAliases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index.nets.TryAdd(entry.Source, entry.Ordinal);
            }

            return index;
        }

        public bool TryGetEvaluator(
            CompilationSource source,
            out SimulationEvaluator evaluator)
        {
            return evaluators.TryGetValue(source, out evaluator!);
        }

        public bool TryGetNetOrdinal(CompilationSource source, out int ordinal)
        {
            return nets.TryGetValue(source, out ordinal);
        }
    }
}

internal sealed record HotSwapStateMigration(
    SimulationEvaluator Current,
    SimulationEvaluator Replacement,
    CompilationSource Source);
