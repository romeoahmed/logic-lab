using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SelectionInspector
{
    private ParameterDraft? parameterDraft;

    private void UpdateParameterDraft(CircuitDefinition? definition, ComponentInstance? component)
    {
        if (component is null)
        {
            parameterDraft = null;
            return;
        }
        if (parameterDraft is { } draft && draft.RevisionId == revisionId
            && draft.DefinitionId == definition!.Id && draft.ComponentId == component.Id)
        {
            return;
        }
        var contract = component.Target is LibraryComponentTarget target
            ? Projection.ProjectRevision.Document.LibrarySnapshot.ResolveContract(target.ContractKey) : null;
        parameterDraft = new(revisionId, definition!.Id, component, component.Target,
            contract is null ? [] : [.. contract.Parameters.Select(schema => new ParameterField(schema,
                component.Parameters.Single(binding => binding.ParameterId == schema.Id).Value))]);
    }

    private async Task ApplyParametersAsync(ParameterDraft draft)
    {
        if (!CanEdit || !ReferenceEquals(parameterDraft, draft) || !draft.Changed || draft.Target != draft.Source.Target)
        {
            return;
        }
        var parameters = new List<ComponentParameterBinding>();
        foreach (var field in draft.Fields)
        {
            if (field.Parse(Projection.ProjectRevision.Document) is not { } value)
            {
                return;
            }
            parameters.Add(new(field.Schema.Id, value));
        }
        await OnEdit.InvokeAsync(new EditRequest(draft.RevisionId, draft.DefinitionId,
            new SetInstanceParametersIntent(draft.DefinitionId, draft.ComponentId, parameters)));
    }

    private void ChangeParameter(ParameterDraft draft, ParameterField field, string? value)
    {
        if (!CanEdit || !ReferenceEquals(parameterDraft, draft) || !draft.Fields.Contains(field)
            || field.Text == (value ?? string.Empty))
        {
            return;
        }
        field.Text = value ?? string.Empty;
        draft.Migration = null;
        draft.MigrationError = null;
    }

    private string ParameterHint(ComponentParameterSchema schema) => Text[schema switch
    {
        WidthsParameterSchema => "InspectorWidthsHint",
        SlicesParameterSchema => "InspectorSlicesHint",
        LogicVectorParameterSchema => "InspectorVectorHint",
        _ => "InspectorUnsignedHint",
    }];

    private sealed record ParameterDraft(ProjectRevisionId RevisionId, CircuitDefinitionId DefinitionId,
        ComponentInstance Source, ComponentTarget Target, IReadOnlyList<ParameterField> Fields)
    {
        public ComponentInstanceId ComponentId => Source.Id;
        public bool Changed => Target != Source.Target || Fields.Any(item => item.Text != item.OriginalText);
        public MigrationPreview? Migration { get; set; }
        public string? MigrationError { get; set; }
    }
}
