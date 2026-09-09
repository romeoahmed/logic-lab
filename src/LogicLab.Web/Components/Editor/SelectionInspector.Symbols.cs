using LogicLab.Domain.Authoring;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SelectionInspector
{
    private SymbolDraft? symbolDraft;

    private void UpdateSymbolDraft(CircuitDefinition? definition, ComponentInstance? component, bool noSelection)
    {
        if (definition is null || (!noSelection && component is null))
        {
            symbolDraft = null;
            return;
        }
        if (symbolDraft is { } current && current.RevisionId == revisionId
            && current.DefinitionId == definition.Id && current.Component?.Id == component?.Id)
        {
            return;
        }
        symbolDraft = new(revisionId, definition.Id, Projection.ProjectRevision.Document.SymbolProfile, component);
    }

    private Task ApplySymbolAsync(SymbolDraft current)
    {
        if (!CanEdit || !ReferenceEquals(symbolDraft, current))
        {
            return Task.CompletedTask;
        }
        EditIntent? intent = null;
        if (current.Component is { } component && current.Variant != (component.SymbolVariantId ?? string.Empty)
            && (current.Variant.Length == 0 || current.Variants.Contains(current.Variant)))
        {
            intent = new SetSymbolVariantIntent(current.DefinitionId, component.Id, current.Variant.Length == 0 ? null : current.Variant);
        }
        else if (current.Component is null && current.SelectedConvention is { } convention
            && convention != current.Profile.IndicationConvention)
        {
            intent = new SetSymbolProfileIntent(current.Profile with { IndicationConvention = convention }, []);
        }
        return intent is null ? Task.CompletedTask : OnEdit.InvokeAsync(new EditRequest(current.RevisionId, current.DefinitionId, intent));
    }

    private string VariantLabel(string variant) => Text[variant switch
    {
        SymbolVariantCatalog.BoundaryId => "InspectorSymbolBoundary",
        SymbolVariantCatalog.DistinctiveId => "InspectorSymbolDistinctive",
        SymbolVariantCatalog.RectangularId => "InspectorSymbolRectangular",
        _ => throw new InvalidOperationException("The symbol variant is undefined."),
    }];

    private sealed class SymbolDraft(ProjectRevisionId revisionId, CircuitDefinitionId definitionId,
        SymbolProfileReference profile, ComponentInstance? component)
    {
        public ProjectRevisionId RevisionId { get; } = revisionId;
        public CircuitDefinitionId DefinitionId { get; } = definitionId;
        public SymbolProfileReference Profile { get; } = profile;
        public ComponentInstance? Component { get; } = component;
        public string Variant { get; set; } = component?.SymbolVariantId ?? string.Empty;
        public string Convention { get; set; } = profile.IndicationConvention.ToString();
        public IReadOnlyList<string> Variants { get; } = component is null ? []
            : SymbolVariantCatalog.GetCompatibleVariants(profile, component.Target, component.Parameters);
        public IndicationConvention? SelectedConvention => Enum.TryParse<IndicationConvention>(Convention, out var value)
            && Enum.IsDefined(value) ? value : null;
        public bool Changed => Component is null
            ? SelectedConvention is { } value && value != Profile.IndicationConvention
            : Variant != (Component.SymbolVariantId ?? string.Empty) && (Variant.Length == 0 || Variants.Contains(Variant));
    }
}
