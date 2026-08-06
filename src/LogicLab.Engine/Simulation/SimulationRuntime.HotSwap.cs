using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    private static SimulationCommandOutcome HotSwap(
        SimulationSessionState state,
        CompilationArtifact replacement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CompilationArtifactValidator.Validate(
            replacement.SimulationIr,
            replacement.SourceMap,
            cancellationToken);
        EnsureWorkingLayerFits(replacement.SimulationIr, state.SimulationPolicy);

        var current = state.Artifact!;
        var unresolvedProbeIds = FindUnresolvedProbeIds(state.Probes, replacement);
        var migrations = FindStateMigrations(current, replacement, cancellationToken);
        if (migrations.IncompatibleSources.Length != 0)
        {
            return new HotSwapIncompatible(
                state.SessionVersion,
                current.Key,
                migrations.IncompatibleSources,
                unresolvedProbeIds);
        }

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
        var probes = RebindProbes(state.Probes, replacement);
        var observedProbes = probes.Select(probe => new ProbeObservation(
            probe.ProbeId,
            probe.Source,
            settlement.NetValues[probe.NetOrdinal])).ToArray();
        var trace = new SimulationTraceStore(state.TracePolicy);
        trace.Append(
            state.LogicalTime,
            [.. probes.Select(probe => (probe, settlement.NetValues[probe.NetOrdinal]))]);
        var clockEvents = CreateClockEventCalendar(replacementIr, state.LogicalTime);
        cancellationToken.ThrowIfCancellationRequested();

        state.Artifact = replacement;
        state.DriverValues = driverValues;
        state.NetValues = settlement.NetValues;
        state.SequentialStates = sequentialStates;
        state.MemoryStates = memoryStates;
        state.Probes = probes;
        state.Trace = trace;
        state.Diagnostics = diagnostics;
        state.SessionVersion = checked(state.SessionVersion + 1);
        state.NextStimulusSequence = 0;
        state.ScheduledBatches = new();
        state.ScheduledAssignmentsByTime = [];
        state.ScheduledAssignmentCount = 0;
        state.ClockEvents = clockEvents;

        var preservedProbeIds = probes.Select(probe => probe.ProbeId).ToArray();
        return new HotSwapCommitted(
            state.SessionVersion,
            replacement.Key,
            new HotSwapMigrationEvidence(
                [.. migrations.Items
                    .Select(item => item.Source)
                    .Order(CompilationSourceComparer.Instance)],
                preservedProbeIds,
                unresolvedProbeIds),
            preservedProbeIds,
            observedProbes,
            diagnostics,
            trace.Cursor);
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
        CompilationArtifact replacement,
        CancellationToken cancellationToken)
    {
        var migrations = new List<StateMigration>();
        var incompatible = new List<CompilationSource>();
        foreach (var evaluator in current.SimulationIr.Evaluators.Where(IsMigratedState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = current.SourceMap.Evaluators[evaluator.Ordinal].Source;
            var replacementEvaluator = FindEvaluator(replacement, source);
            if (replacementEvaluator is null
                || !HasCompatibleStateSchema(evaluator, replacementEvaluator))
            {
                incompatible.Add(source);
                continue;
            }

            migrations.Add(new StateMigration(evaluator, replacementEvaluator, source));
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

    private static SimulationEvaluator? FindEvaluator(
        CompilationArtifact artifact,
        CompilationSource source)
    {
        foreach (var entry in artifact.SourceMap.Evaluators)
        {
            if (CompilationSourceComparer.Instance.Compare(entry.Source, source) == 0)
            {
                return artifact.SimulationIr.Evaluators[entry.Ordinal];
            }
        }

        return null;
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

    private static ProbeId[] FindUnresolvedProbeIds(
        IEnumerable<ProbeState> probes,
        CompilationArtifact replacement)
    {
        return [.. probes
            .Where(probe => !replacement.SourceMap.TryGetNetOrdinal(
                probe.Source,
                out _))
            .Select(probe => probe.ProbeId)];
    }

    private static ProbeState[] RebindProbes(
        IEnumerable<ProbeState> probes,
        CompilationArtifact replacement)
    {
        var rebound = new List<ProbeState>();
        foreach (var probe in probes)
        {
            if (replacement.SourceMap.TryGetNetOrdinal(probe.Source, out var netOrdinal))
            {
                rebound.Add(new ProbeState(probe.ProbeId, probe.Source, netOrdinal));
            }
        }

        return [.. rebound];
    }

    private sealed record StateMigration(
        SimulationEvaluator Current,
        SimulationEvaluator Replacement,
        CompilationSource Source);

    private sealed record StateMigrationResult(
        StateMigration[] Items,
        CompilationSource[] IncompatibleSources);
}
