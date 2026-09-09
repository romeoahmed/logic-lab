using System.Globalization;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SelectionInspector
{
    private AnnotationDraft? annotationDraft;

    private void UpdateAnnotationDraft(CircuitDefinition? definition, IReadOnlyList<SceneSourceRefV1> sources)
    {
        var annotation = sources.Count == 1 && sources[0].EntityKind == "annotation"
            && sources[0].CircuitDefinitionId == definition?.Id.Value
            ? definition.Annotations.FirstOrDefault(item => item.Id.Value == sources[0].EntityId) : null;
        if (definition is null || (sources.Count != 0 && annotation is null))
        {
            annotationDraft = null;
            return;
        }
        if (annotationDraft is { } current && current.RevisionId == revisionId
            && current.DefinitionId == definition.Id && current.Source?.Id == annotation?.Id)
        {
            return;
        }
        annotationDraft = new(revisionId, definition.Id, annotation);
    }

    private Task ApplyAnnotationAsync(AnnotationDraft current) => CanEdit && ReferenceEquals(annotationDraft, current)
        && current.Changed && current.Value is { } value
        ? OnEdit.InvokeAsync(new EditRequest(current.RevisionId, current.DefinitionId,
            current.Source is { } source
                ? new ChangeAnnotationIntent(current.DefinitionId, source.Id, value)
                : new CreateAnnotationIntent(current.DefinitionId, value)))
        : Task.CompletedTask;

    private sealed class AnnotationDraft(ProjectRevisionId revisionId, CircuitDefinitionId definitionId, Annotation? source)
    {
        public ProjectRevisionId RevisionId { get; } = revisionId;
        public CircuitDefinitionId DefinitionId { get; } = definitionId;
        public Annotation? Source { get; } = source;
        public string Text { get; set; } = source?.Text ?? string.Empty;
        public string X { get; set; } = (source?.Position.X ?? 0).ToString(CultureInfo.InvariantCulture);
        public string Y { get; set; } = (source?.Position.Y ?? 0).ToString(CultureInfo.InvariantCulture);
        public string Alignment { get; set; } = (source?.Alignment ?? AnnotationAlignment.Start).ToString();
        public AnnotationValue? Value => int.TryParse(X.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(Y.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
            && Enum.TryParse<AnnotationAlignment>(Alignment, out var alignment) && Enum.IsDefined(alignment)
            ? new(Text, new(x, y), alignment) : null;
        public bool Changed => Source is null ? Text.Length != 0
            : Value != new AnnotationValue(Source.Text, Source.Position, Source.Alignment);
    }
}
