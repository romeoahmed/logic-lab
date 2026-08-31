using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private void NormalizeHierarchyNavigation(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (SelectedDefinitionId is null)
        {
            SelectedDefinitionId = document.EntryCircuitDefinitionId;
            HierarchyNavigation.Clear();
            return;
        }

        if (HierarchyNavigation.Count == 0)
        {
            if (document.FindCircuitDefinition(SelectedDefinitionId) is null)
            {
                SelectedDefinitionId = document.EntryCircuitDefinitionId;
            }
            return;
        }

        var current = document.EntryCircuitDefinition;
        var normalized = new List<HierarchyNavigationStep>(HierarchyNavigation.Count);
        foreach (var step in HierarchyNavigation)
        {
            var instance = step.ContainingCircuitDefinitionId == current.Id
                ? current.FindComponentInstance(step.ComponentInstanceId)
                : null;
            if (instance?.Target is not CircuitDefinitionComponentTarget target
                || document.FindCircuitDefinition(target.CircuitDefinitionId)
                    is not { } child)
            {
                break;
            }

            normalized.Add(new HierarchyNavigationStep(
                current.Id,
                instance.Id,
                instance.DisplayName ?? child.DisplayName));
            current = child;
        }

        HierarchyNavigation.Clear();
        HierarchyNavigation.AddRange(normalized);
        SelectedDefinitionId = current.Id;
    }

    private SceneHierarchyPathV1? CurrentSceneHierarchyPath
    {
        get
        {
            if (Projection is null || SelectedDefinitionId is null)
            {
                return null;
            }

            var entryId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
            if (HierarchyNavigation.Count == 0 && SelectedDefinitionId != entryId)
            {
                return null;
            }

            return new SceneHierarchyPathV1(
                entryId.Value,
                [.. HierarchyNavigation.Select(step => new SceneHierarchyStepV1(
                    step.ContainingCircuitDefinitionId.Value,
                    step.ComponentInstanceId.Value))]);
        }
    }

    private IReadOnlyList<HierarchyBreadcrumbItem> Breadcrumbs
    {
        get
        {
            if (Projection is null || SelectedDefinitionId is null)
            {
                return [];
            }

            var document = Projection.ProjectRevision.Document;
            if (HierarchyNavigation.Count == 0)
            {
                var selected = document.FindCircuitDefinition(SelectedDefinitionId)!;
                return [new HierarchyBreadcrumbItem(
                    $"definition:{selected.Id.Value}",
                    selected.DisplayName)];
            }

            return
            [
                new HierarchyBreadcrumbItem(
                    $"definition:{document.EntryCircuitDefinitionId.Value}",
                    document.EntryCircuitDefinition.DisplayName),
                .. HierarchyNavigation.Select((step, index) => new HierarchyBreadcrumbItem(
                    $"instance:{index}:{step.ContainingCircuitDefinitionId.Value}:{step.ComponentInstanceId.Value}",
                    step.DisplayName)),
            ];
        }
    }

    private void SelectDefinition(CircuitDefinitionId definitionId)
    {
        if (Projection?.ProjectRevision.Document.FindCircuitDefinition(definitionId) is null)
        {
            return;
        }

        SelectedDefinitionId = definitionId;
        HierarchyNavigation.Clear();
        ProjectScene();
        Status = Text["EditingDefinition", SelectedDefinition!.DisplayName];
    }

    private Task SetEntryDefinition(CircuitDefinitionId definitionId)
    {
        return RunCommandAsync(
            "set-entry",
            () => CanSetEntryDefinition
                && Projection?.ProjectRevision.Document.FindCircuitDefinition(definitionId)
                    is not null
                && Projection.ProjectRevision.Document.EntryCircuitDefinitionId != definitionId,
            () => SetEntryDefinitionCore(definitionId));
    }

    private async Task SetEntryDefinitionCore(CircuitDefinitionId definitionId)
    {
        if (await Apply(new SetEntryCircuitDefinitionIntent(definitionId)))
        {
            SelectedDefinitionId = definitionId;
            HierarchyNavigation.Clear();
            ProjectScene();
            Status = Text["EntryDefinitionChanged", SelectedDefinition!.DisplayName];
        }
    }

    private void EnterDefinitionInstance(ComponentInstanceId instanceId)
    {
        if (Projection is null || SelectedDefinitionId is null)
        {
            return;
        }

        if (HierarchyNavigation.Count == 0
            && SelectedDefinitionId != Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId)
        {
            return;
        }

        var containing = Projection.ProjectRevision.Document
            .FindCircuitDefinition(SelectedDefinitionId);
        var instance = containing?.FindComponentInstance(instanceId);
        if (instance?.Target is not CircuitDefinitionComponentTarget target)
        {
            return;
        }

        var targetDefinition = Projection.ProjectRevision.Document.FindCircuitDefinition(
            target.CircuitDefinitionId)!;
        HierarchyNavigation.Add(new HierarchyNavigationStep(
            containing!.Id,
            instance.Id,
            instance.DisplayName ?? targetDefinition.DisplayName));
        SelectedDefinitionId = targetDefinition.Id;
        ProjectScene();
        Status = Text[
            "HierarchyObserving",
            string.Join(" / ", Breadcrumbs.Select(item => item.Label))];
    }

    private void LeaveDefinitionInstance()
    {
        if (HierarchyNavigation.Count == 0)
        {
            return;
        }

        var last = HierarchyNavigation[^1];
        HierarchyNavigation.RemoveAt(HierarchyNavigation.Count - 1);
        SelectedDefinitionId = last.ContainingCircuitDefinitionId;
        ProjectScene();
        Status = Text["HierarchyReturned", SelectedDefinition!.DisplayName];
    }

    private sealed record HierarchyNavigationStep(
        CircuitDefinitionId ContainingCircuitDefinitionId,
        ComponentInstanceId ComponentInstanceId,
        string DisplayName);
}
