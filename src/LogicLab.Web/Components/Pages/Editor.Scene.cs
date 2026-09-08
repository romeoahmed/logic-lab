using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private Components.Editor.WorkbenchDock? workbenchDock;

    private async Task ChangePaletteToolAsync(SceneToolV1 tool)
    {
        await ChangeSceneToolAsync(tool);
        if (workbenchDock is not null)
        {
            await workbenchDock.CloseAsync();
        }
    }

    private Task HandleSceneIntentAsync(SceneIntentV1 intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return RunCommandAsync("scene-edit", () => CanMutateWorkspace,
            () => ApplySceneIntentAsync(intent));
    }

    private async Task ApplySceneIntentAsync(SceneIntentV1 intent)
    {
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
        if (!CanMutateWorkspace || Projection?.Simulation is not { } simulation)
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

    private SceneSelectionV1? SceneSelection { get; set; }

    private SceneToolV1 SceneTool { get; set; } = SceneSelectToolV1.Instance;

    private IReadOnlyList<ScenePlaceOptionV1> ScenePlaceOptions { get; set; } = [];

    private CircuitDefinitionId? SelectedDefinitionId { get; set; }

    private CircuitDefinition? SelectedDefinition => Projection is null
        || SelectedDefinitionId is null
            ? null
            : Projection.ProjectRevision.Document.FindCircuitDefinition(SelectedDefinitionId);

    private List<HierarchyNavigationStep> HierarchyNavigation { get; } = [];

    private async Task<bool> Apply(EditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var projection = Projection;
        if (projection is null || !CanMutateWorkspace)
        {
            return false;
        }

        var precondition = new AuthoringPrecondition(
            projection.ProjectRevision.RevisionId);
        var outcome = await Execute(context => new ApplyEdit(
            context,
            precondition,
            intent));
        if (outcome is AuthoringCommitted)
        {
            return Projection is not null;
        }

        if (Projection is null)
        {
            return false;
        }

        Status = Text["AuthoringRejected", ((WorkspaceCommandRejected)outcome).Code];
        return false;
    }

    private void ProjectScene()
    {
        if (Projection is null)
        {
            ScenePlaceOptions = [];
            SceneTool = SceneSelectToolV1.Instance;
            return;
        }

        var document = Projection.ProjectRevision.Document;
        NormalizeHierarchyNavigation(document);
        _ = SelectedDefinitionId
            ?? throw new InvalidOperationException("The Scene definition is unavailable.");
        ScenePlaceOptions = ScenePlaceCatalog.Build(document);
        EnsureSceneToolAvailable();
        NormalizeSceneSelection();
    }

    private Task ChangeSceneToolAsync(SceneToolV1 tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!CanMutateWorkspace && tool is not (SceneSelectToolV1 or ScenePanToolV1))
        {
            return Task.CompletedTask;
        }

        if (tool is SceneProbeToolV1 && !CanUseSceneProbe)
        {
            return Task.CompletedTask;
        }

        SceneTool = tool;
        return Task.CompletedTask;
    }

    private void EnsureSceneToolAvailable()
    {
        if (!CanMutateWorkspace && SceneTool is not (SceneSelectToolV1 or ScenePanToolV1))
        {
            SceneTool = SceneSelectToolV1.Instance;
            return;
        }

        if (SceneTool is ScenePlaceToolV1 place
            && !ScenePlaceOptions.Any(option => option.Tool.Target == place.Target))
        {
            SceneTool = SceneSelectToolV1.Instance;
            return;
        }

        if (SceneTool is not SceneProbeToolV1)
        {
            return;
        }

        if (Projection?.Simulation is null || CurrentSceneHierarchyPath is not { } hierarchyPath)
        {
            SceneTool = SceneSelectToolV1.Instance;
            return;
        }

        SceneTool = new SceneProbeToolV1(hierarchyPath);
    }

    private Task ConsumeSceneToolAsync()
    {
        SceneTool = SceneSelectToolV1.Instance;
        return Task.CompletedTask;
    }

    private Task HandleSceneSelectionAsync(SceneSelectionV1 change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var selected = SceneSelection?.Sources.ToList() ?? [];
        switch (change.SelectionMode)
        {
            case "replace":
                selected = [.. change.Sources];
                break;
            case "add":
                foreach (var source in change.Sources)
                {
                    if (!selected.Any(candidate => candidate.Key == source.Key))
                    {
                        selected.Add(source);
                    }
                }
                break;
            case "toggle":
                foreach (var source in change.Sources)
                {
                    var index = selected.FindIndex(candidate => candidate.Key == source.Key);
                    if (index >= 0)
                    {
                        selected.RemoveAt(index);
                    }
                    else
                    {
                        selected.Add(source);
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(change),
                    change.SelectionMode,
                    "The Scene selection mode is undefined.");
        }

        SceneSelection = selected.Count == 0
            ? null
            : new SceneSelectionV1(selected, "replace");
        return Task.CompletedTask;
    }

    private void NormalizeSceneSelection()
    {
        if (SceneSelection is null || Projection is null || SelectedDefinitionId is null)
        {
            SceneSelection = null;
            return;
        }

        var retained = SceneSelection.Sources
            .Where(source => source.CircuitDefinitionId == SelectedDefinitionId?.Value
                && SceneSourceMap.Contains(Projection.ProjectRevision, source))
            .ToArray();
        SceneSelection = retained.Length == 0
            ? null
            : new SceneSelectionV1(retained, "replace");
    }
}
