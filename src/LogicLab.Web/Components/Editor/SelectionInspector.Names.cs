using LogicLab.Domain.Authoring;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SelectionInspector
{
    private NameDraft? nameDraft;

    private void UpdateNameDraft(CircuitDefinition? definition, ComponentInstance? component, bool noSelection)
    {
        if (definition is null || (!noSelection && component is null))
        {
            nameDraft = null;
            return;
        }
        if (nameDraft is { } draft && draft.RevisionId == revisionId
            && draft.DefinitionId == definition.Id && draft.ComponentId == component?.Id)
        {
            return;
        }
        nameDraft = new(revisionId, definition.Id, component?.Id,
            component is null ? definition.DisplayName : component.DisplayName ?? string.Empty);
    }

    private Task RenameAsync(NameDraft draft) => CanEdit && ReferenceEquals(nameDraft, draft)
        && draft.Name != draft.OriginalName
        ? OnEdit.InvokeAsync(new EditRequest(draft.RevisionId, draft.DefinitionId,
            draft.ComponentId is { } componentId
                ? new RenameComponentInstanceIntent(draft.DefinitionId, componentId,
                    draft.Name.Length == 0 ? null : draft.Name)
                : new RenameCircuitDefinitionIntent(draft.DefinitionId, draft.Name)))
        : Task.CompletedTask;

    private bool CanRemoveDefinition(NameDraft draft) => draft.ComponentId is null
        && Projection.ProjectRevision.Document.EntryCircuitDefinitionId != draft.DefinitionId
        && !Projection.ProjectRevision.Document.CircuitDefinitions.SelectMany(definition => definition.ComponentInstances)
            .Any(instance => instance.Target is CircuitDefinitionComponentTarget target && target.CircuitDefinitionId == draft.DefinitionId);

    private Task CreateDefinitionAsync(NameDraft draft) => CanEdit && ReferenceEquals(nameDraft, draft)
        && draft.ComponentId is null && !string.IsNullOrWhiteSpace(draft.NewDefinitionName)
        ? OnEdit.InvokeAsync(new EditRequest(draft.RevisionId, draft.DefinitionId,
            new CreateCircuitDefinitionIntent(draft.NewDefinitionName, []))) : Task.CompletedTask;

    private Task RemoveDefinitionAsync(NameDraft draft) => CanEdit && ReferenceEquals(nameDraft, draft) && CanRemoveDefinition(draft)
        ? OnEdit.InvokeAsync(new EditRequest(draft.RevisionId, draft.DefinitionId,
            new RemoveCircuitDefinitionIntent(draft.DefinitionId))) : Task.CompletedTask;

    private sealed class NameDraft(ProjectRevisionId revisionId, CircuitDefinitionId definitionId,
        ComponentInstanceId? componentId, string originalName)
    {
        public ProjectRevisionId RevisionId { get; } = revisionId;
        public CircuitDefinitionId DefinitionId { get; } = definitionId;
        public ComponentInstanceId? ComponentId { get; } = componentId;
        public string OriginalName { get; } = originalName;
        public string Name { get; set; } = originalName;
        public string NewDefinitionName { get; set; } = string.Empty;
    }
}
