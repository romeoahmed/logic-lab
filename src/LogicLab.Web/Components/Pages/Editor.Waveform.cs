using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private async Task<TraceWindowOutcome?> ReadTraceWindowAsync(
        TraceWindowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reattachAttempted = false;
        while (Projection?.Simulation is { } simulation
            && simulation.SessionId == request.SessionId
            && simulation.CompilationArtifactKey == request.CompilationArtifactKey)
        {
            var read = await workspace.ReadAsync(
                QueryContext(),
                new ReadTraceWindow(request),
                cancellationToken);
            if (read is TraceWindowRead trace)
            {
                return trace.Outcome;
            }

            if (read is WorkspaceReadRejected
                {
                    RetryDisposition: RetryDisposition.Reattach,
                }
                && !reattachAttempted
                && await TryReattachAsync(cancellationToken))
            {
                reattachAttempted = true;
                continue;
            }

            return null;
        }

        return null;
    }

    private Task ReorderProbesAsync(IReadOnlyList<string> orderedProbeIds)
    {
        ArgumentNullException.ThrowIfNull(orderedProbeIds);
        if (Projection?.Simulation is not { } simulation
            || orderedProbeIds.Count != simulation.Probes.Count
            || orderedProbeIds.Distinct(StringComparer.Ordinal).Count()
                != orderedProbeIds.Count)
        {
            return Task.CompletedTask;
        }

        var probeById = simulation.Probes.ToDictionary(
            probe => probe.ProbeId.Value,
            StringComparer.Ordinal);
        var bindings = new ProbeBindingRequest[orderedProbeIds.Count];
        for (var index = 0; index < orderedProbeIds.Count; index++)
        {
            if (!probeById.TryGetValue(orderedProbeIds[index], out var probe))
            {
                return Task.CompletedTask;
            }

            bindings[index] = new RetainProbe(probe.ProbeId, probe.Source);
        }

        return ReplaceProbesAsync(bindings);
    }

    private Task RemoveProbeAsync(string probeId)
    {
        if (Projection?.Simulation is not { } simulation
            || !simulation.Probes.Any(probe => probe.ProbeId.Value == probeId))
        {
            return Task.CompletedTask;
        }

        return ReplaceProbesAsync([.. simulation.Probes
            .Where(probe => probe.ProbeId.Value != probeId)
            .Select(probe => (ProbeBindingRequest)new RetainProbe(
                probe.ProbeId,
                probe.Source))]);
    }

    private Task RebindProbeAsync(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Projection?.Simulation is not { } simulation
            || simulation.Probes.Any(probe => probe.Source == source))
        {
            return Task.CompletedTask;
        }

        return ReplaceProbesAsync(
        [
            .. simulation.Probes.Select(probe =>
                (ProbeBindingRequest)new RetainProbe(probe.ProbeId, probe.Source)),
            new CreateProbe(source),
        ]);
    }

    private async Task ReplaceProbesAsync(IReadOnlyList<ProbeBindingRequest> bindings)
    {
        if (!CanMutateWorkspace)
        {
            return;
        }

        var outcome = await Execute(context => new ReplaceProbes(
            context,
            SessionPrecondition(),
            bindings));
        if (outcome is WorkspaceCommandRejected rejected)
        {
            Status = Text["SessionRejected", rejected.Code];
        }
    }

    private Task RevealProbeSourceAsync(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Identity is not NetSourceIdentity || !TryRevealSource(source))
        {
            return Task.CompletedTask;
        }

        Status = Text[
            "ProbeRevealed",
            ProbePresentation.Label(
                Projection!.ProjectRevision,
                source,
                new ProbePresentationLabels(
                    Text["ComponentInput"],
                    Text["ComponentOutput"]))];
        return Task.CompletedTask;
    }
}
