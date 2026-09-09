using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private Task HandleInspectorEditAsync(SelectionInspector.EditRequest request) => RunCommandAsync(
        "inspector-edit",
        () => !showMemoryImages && CanMutateWorkspace
            && Projection?.ProjectRevision.RevisionId == request.RevisionId
            && SelectedDefinitionId == request.DefinitionId,
        async () =>
        {
            if (await Apply(request.Intent))
            {
                if (request.Intent is CreateCircuitDefinitionIntent)
                {
                    SelectDefinition(Projection!.ProjectRevision.Document.CircuitDefinitions[^1].Id);
                }
                else if (request.Intent is CreateAnnotationIntent)
                {
                    var annotation = Projection!.ProjectRevision.Document.FindCircuitDefinition(request.DefinitionId)!.Annotations[^1];
                    SceneSelection = new SceneSelectionV1([SceneSourceMap.From(new AnnotationSourceIdentity(request.DefinitionId, annotation.Id))], "replace");
                }
                Status = Text["InspectorEditApplied"];
            }
        });
}
