using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
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
        if (orderedProbeIds.Any(probeId => !probeById.ContainsKey(probeId)))
        {
            return Task.CompletedTask;
        }

        return ReplaceProbesAsync([.. orderedProbeIds.Select(probeId =>
            (ProbeBindingRequest)new RetainProbe(
                probeById[probeId].ProbeId,
                probeById[probeId].Source))]);
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
        if (Projection is not { } projection
            || source.Identity is not NetSourceIdentity netSource
            || source.HierarchyPath.EntryCircuitDefinitionId
                != projection.ProjectRevision.Document.EntryCircuitDefinitionId
            || !SceneSourceMap.Contains(
                projection.ProjectRevision,
                SceneSourceMap.From(netSource)))
        {
            return Task.CompletedTask;
        }

        var document = projection.ProjectRevision.Document;
        var current = document.EntryCircuitDefinition;
        var navigation = new List<HierarchyNavigationStep>(
            source.HierarchyPath.Steps.Count);
        foreach (var step in source.HierarchyPath.Steps)
        {
            var instance = step.ContainingCircuitDefinitionId == current.Id
                ? current.FindComponentInstance(step.ComponentInstanceId)
                : null;
            if (instance?.Target is not CircuitDefinitionComponentTarget target
                || document.FindCircuitDefinition(target.CircuitDefinitionId)
                    is not { } child)
            {
                return Task.CompletedTask;
            }

            navigation.Add(new HierarchyNavigationStep(
                current.Id,
                instance.Id,
                instance.DisplayName ?? child.DisplayName));
            current = child;
        }

        if (current.Id != netSource.CircuitDefinitionId)
        {
            return Task.CompletedTask;
        }

        HierarchyNavigation.Clear();
        HierarchyNavigation.AddRange(navigation);
        SelectedDefinitionId = current.Id;
        ProjectScene();
        SceneSelection = new SceneSelectionV1(
            [SceneSourceMap.From(netSource)],
            "replace");
        Status = Text[
            "ProbeRevealed",
            ProbePresentation.Label(projection.ProjectRevision, source, 0)];
        return Task.CompletedTask;
    }
}
