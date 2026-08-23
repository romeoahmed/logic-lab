using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    private static SimulationCommandOutcome ReplaceProbes(
        SimulationSessionState state,
        ReplaceProbeBindings command,
        CancellationToken cancellationToken)
    {
        var bindings = command.Bindings;
        if ((ulong)bindings.Count > state.TracePolicy.Maximum(TraceDimension.ProbeCount))
        {
            return new SimulationCommandFailed(
                state.SessionVersion,
                state.LogicalTime,
                SimulationFailureReason.SimulationResourceLimit,
                [],
                new SimulationPolicyEvidence(
                    state.TracePolicy.PolicyId,
                    state.TracePolicy.PolicyRevision,
                    DimensionToken(TraceDimension.ProbeCount),
                    (ulong)bindings.Count));
        }

        var activeById = state.Probes.ToDictionary(probe => probe.ProbeId);
        var firstSourceByProbeId = new Dictionary<ProbeId, CompilationSource>();
        var firstSourceByNetOrdinal = new Dictionary<int, CompilationSource>();
        var requestedSources = new HashSet<CompilationSource>();
        var replacement = new ProbeState[bindings.Count];
        var createdObservations = new List<(ProbeState Probe, LogicVector Value)>();

        for (var index = 0; index < bindings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = bindings[index];
            if (!requestedSources.Add(binding.Source))
            {
                return InvalidProbeBindings(
                    state,
                    ProbeBindingsInvalidRule.DuplicateBinding,
                    binding.Source);
            }

            ProbeId probeId;
            switch (binding)
            {
                case RetainProbe retain:
                    if (firstSourceByProbeId.TryGetValue(
                            retain.ProbeId,
                            out var firstRetainedSource))
                    {
                        return InvalidProbeBindings(
                            state,
                            ProbeBindingsInvalidRule.DuplicateBinding,
                            firstRetainedSource,
                            retain.Source);
                    }

                    firstSourceByProbeId.Add(retain.ProbeId, retain.Source);
                    if (!activeById.TryGetValue(retain.ProbeId, out var activeProbe))
                    {
                        return InvalidProbeBindings(
                            state,
                            ProbeBindingsInvalidRule.ArtifactMismatch,
                            retain.Source);
                    }

                    if (activeProbe.Source != retain.Source)
                    {
                        return InvalidProbeBindings(
                            state,
                            ProbeBindingsInvalidRule.ArtifactMismatch,
                            activeProbe.Source,
                            retain.Source);
                    }

                    probeId = retain.ProbeId;
                    break;
                case CreateProbe:
                    probeId = ProbeId.Create();
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Probe binding request variant is undefined.");
            }

            if (!state.Artifact!.SourceMap.TryGetNetOrdinal(
                    binding.Source,
                    out var netOrdinal))
            {
                return InvalidProbeBindings(
                    state,
                    ProbeBindingsInvalidRule.UnresolvedSource,
                    binding.Source);
            }

            if (firstSourceByNetOrdinal.TryGetValue(
                    netOrdinal,
                    out var firstNetSource))
            {
                return InvalidProbeBindings(
                    state,
                    ProbeBindingsInvalidRule.DuplicateBinding,
                    firstNetSource,
                    binding.Source);
            }

            firstSourceByNetOrdinal.Add(netOrdinal, binding.Source);
            var probe = new ProbeState(probeId, binding.Source, netOrdinal);
            replacement[index] = probe;
            if (binding is CreateProbe)
            {
                createdObservations.Add((probe, state.NetValues[netOrdinal]));
            }
        }

        var nextVersion = checked(state.SessionVersion + 1UL);
        cancellationToken.ThrowIfCancellationRequested();
        var nextTrace = createdObservations.Count == 0
            ? state.Trace
            : state.Trace.ForkWithAppend(state.LogicalTime, createdObservations);

        state.Probes = replacement;
        state.Trace = nextTrace;
        state.SessionVersion = nextVersion;
        return new ProbeBindingsReplaced(
            state.SessionVersion,
            [.. replacement.Select(probe => probe.ProbeId)],
            state.Trace.Cursor);
    }

    private static ProbeBindingsInvalid InvalidProbeBindings(
        SimulationSessionState state,
        ProbeBindingsInvalidRule rule,
        params CompilationSource[] sourceLocations)
    {
        var canonical = sourceLocations
            .Distinct()
            .Order(CompilationSourceComparer.Instance)
            .ToArray();
        return new ProbeBindingsInvalid(
            state.SessionVersion,
            rule,
            canonical);
    }
}
