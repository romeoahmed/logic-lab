using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    private static StateMigrationPreflight InspectStateMigrations(
        CompilationArtifact current,
        CompilationArtifact replacement,
        CancellationToken cancellationToken)
    {
        var migrationCount = 0;
        var incompatibleCount = 0;
        ulong migratedRamCellReferenceCount = 0;
        foreach (var evaluator in current.SimulationIr.Evaluators.Where(IsMigratedState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCreateStateMigration(
                    current,
                    replacement,
                    evaluator,
                    out var migration))
            {
                incompatibleCount++;
                continue;
            }

            migrationCount++;
            if (evaluator.Kind == SimulationEvaluatorKind.MemoryRamSinglePort)
            {
                migratedRamCellReferenceCount = checked(
                    migratedRamCellReferenceCount
                    + (ulong)migration.Replacement.InitialMemory!.Count);
            }
        }

        return new StateMigrationPreflight(
            migrationCount,
            incompatibleCount,
            migratedRamCellReferenceCount);
    }

    private static HotSwapStateMigration[] CreateStateMigrations(
        CompilationArtifact current,
        CompilationArtifact replacement,
        int migrationCount,
        CancellationToken cancellationToken)
    {
        var migrations = new HotSwapStateMigration[migrationCount];
        var migrationIndex = 0;
        foreach (var evaluator in current.SimulationIr.Evaluators.Where(IsMigratedState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCreateStateMigration(
                    current,
                    replacement,
                    evaluator,
                    out var migration))
            {
                throw new InvalidOperationException(
                    "A compatible Hot Swap preflight changed before materialization.");
            }

            migrations[migrationIndex++] = migration;
        }

        return migrations;
    }

    private static CompilationSource[] FindIncompatibleStateSources(
        CompilationArtifact current,
        CompilationArtifact replacement,
        int incompatibleCount,
        CancellationToken cancellationToken)
    {
        var incompatible = new CompilationSource[incompatibleCount];
        var incompatibleIndex = 0;
        foreach (var evaluator in current.SimulationIr.Evaluators.Where(IsMigratedState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = current.SourceMap.Evaluators[evaluator.Ordinal].Source;
            if (!TryCreateStateMigration(
                    current,
                    replacement,
                    evaluator,
                    out _))
            {
                incompatible[incompatibleIndex++] = source;
            }
        }

        Array.Sort(incompatible, CompilationSourceComparer.Instance);
        return incompatible;
    }

    private static bool TryCreateStateMigration(
        CompilationArtifact current,
        CompilationArtifact replacement,
        SimulationEvaluator evaluator,
        out HotSwapStateMigration migration)
    {
        var source = current.SourceMap.Evaluators[evaluator.Ordinal].Source;
        if (TryGetReplacementEvaluator(replacement, source, out var replacementEvaluator)
            && HasCompatibleStateSchema(evaluator, replacementEvaluator))
        {
            migration = new HotSwapStateMigration(
                evaluator,
                replacementEvaluator,
                source);
            return true;
        }

        migration = default;
        return false;
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

    private static ProbeBindingPreflight InspectProbeBindings(
        ProbeState[] probes,
        SourceMap replacementSources,
        CancellationToken cancellationToken)
    {
        var preservedProbeCount = 0;
        var boundNetOrdinals = new HashSet<int>();
        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryResolveProbe(
                    probe,
                    replacementSources,
                    boundNetOrdinals,
                    out _))
            {
                preservedProbeCount++;
            }
        }

        return new ProbeBindingPreflight(preservedProbeCount);
    }

    private static ProbeBindingResult RebindProbes(
        ProbeState[] probes,
        SourceMap replacementSources,
        int preservedProbeCount,
        CancellationToken cancellationToken)
    {
        var rebound = new ProbeState[preservedProbeCount];
        var unresolved = new ProbeId[probes.Length - preservedProbeCount];
        var boundNetOrdinals = new HashSet<int>();
        var reboundIndex = 0;
        var unresolvedIndex = 0;
        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveProbe(
                    probe,
                    replacementSources,
                    boundNetOrdinals,
                    out var netOrdinal))
            {
                unresolved[unresolvedIndex++] = probe.ProbeId;
                continue;
            }

            rebound[reboundIndex++] = new ProbeState(
                probe.ProbeId,
                probe.Source,
                netOrdinal);
        }

        return new ProbeBindingResult(rebound, unresolved);
    }

    private static ProbeId[] FindUnresolvedProbeIds(
        ProbeState[] probes,
        SourceMap replacementSources,
        int unresolvedProbeCount,
        CancellationToken cancellationToken)
    {
        var unresolved = new ProbeId[unresolvedProbeCount];
        var boundNetOrdinals = new HashSet<int>();
        var unresolvedIndex = 0;
        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveProbe(
                    probe,
                    replacementSources,
                    boundNetOrdinals,
                    out _))
            {
                unresolved[unresolvedIndex++] = probe.ProbeId;
            }
        }

        return unresolved;
    }

    private static bool TryResolveProbe(
        ProbeState probe,
        SourceMap replacementSources,
        HashSet<int> boundNetOrdinals,
        out int netOrdinal)
    {
        return replacementSources.TryGetNetOrdinal(probe.Source, out netOrdinal)
            && boundNetOrdinals.Add(netOrdinal);
    }

    private static bool TryGetReplacementEvaluator(
        CompilationArtifact replacement,
        CompilationSource source,
        out SimulationEvaluator evaluator)
    {
        if (replacement.SourceMap.TryGetEvaluatorOrdinal(source, out var ordinal))
        {
            evaluator = replacement.SimulationIr.Evaluators[ordinal];
            return true;
        }

        evaluator = null!;
        return false;
    }

    private readonly record struct StateMigrationPreflight(
        int MigrationCount,
        int IncompatibleCount,
        ulong MigratedRamCellReferenceCount);

    private readonly record struct ProbeBindingPreflight(
        int PreservedProbeCount);

    private sealed record ProbeBindingResult(
        ProbeState[] Probes,
        ProbeId[] UnresolvedProbeIds);
}

internal readonly record struct HotSwapStateMigration(
    SimulationEvaluator Current,
    SimulationEvaluator Replacement,
    CompilationSource Source);
