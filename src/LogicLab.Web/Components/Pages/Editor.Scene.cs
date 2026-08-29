using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private async Task HandleSceneSemanticActionAsync(SceneSemanticActionV1 action)
    {
        if (action is not RemoveSceneSemanticActionV1 remove
            || Projection is not { } projection
            || SelectedDefinitionId is not { } definitionId
            || !string.Equals(
                remove.Source.CircuitDefinitionId,
                definitionId.Value,
                StringComparison.Ordinal)
            || projection.ProjectRevision.Document.FindCircuitDefinition(definitionId)
                is not { } definition)
        {
            return;
        }

        var translator = new SceneIntentTranslator(
            projection.ProjectRevision.Document,
            definition);
        if (translator.TranslateRemoval(remove.Source) is { } intent)
        {
            _ = await Apply(intent);
        }
    }

    private async Task HandleSceneIntentAsync(SceneIntentV1 intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try
        {
            var definition = ResolveIntentDefinition(intent);
            var translator = new SceneIntentTranslator(
                Projection!.ProjectRevision.Document,
                definition);
            if (intent is ToggleProbeSceneIntentV1 toggleProbe)
            {
                await ToggleProbeAsync(translator.TranslateProbe(toggleProbe.Net));
                return;
            }

            _ = await Apply(translator.TranslateEdit(intent));
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException
            or InvalidOperationException
            or OverflowException)
        {
            // Browser input is untrusted. CircuitSceneHost already invalidated its
            // publication key, so an invalid known intent receives a full snapshot.
            return;
        }
    }

    private async Task ToggleProbeAsync(CompilationSource target)
    {
        if (Projection?.Simulation is not { } simulation)
        {
            return;
        }

        var bindings = new List<ProbeBindingRequest>(simulation.Probes.Count + 1);
        var removed = false;
        foreach (var probe in simulation.Probes)
        {
            if (probe.Source == target)
            {
                removed = true;
                continue;
            }

            bindings.Add(new RetainProbe(probe.ProbeId, probe.Source));
        }

        if (!removed)
        {
            bindings.Add(new CreateProbe(target));
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

    private CircuitDefinition ResolveIntentDefinition(SceneIntentV1 intent)
    {
        var projection = Projection
            ?? throw new InvalidOperationException("The Workspace is not open.");
        if (projection.ProjectionVersion != intent.ProjectionVersion
            || SelectedDefinitionId is null
            || !string.Equals(
                SelectedDefinitionId.Value,
                intent.CircuitDefinitionId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Scene Intent is stale.");
        }

        return projection.ProjectRevision.Document.FindCircuitDefinition(SelectedDefinitionId)
            ?? throw new InvalidOperationException("The Scene Circuit Definition is missing.");
    }
}
