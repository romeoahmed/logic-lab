using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private Task HandleInspectorEditAsync(SelectionInspector.EditRequest request) => RunCommandAsync(
        "inspector-edit",
        () => CanMutateWorkspace
            && Projection?.ProjectRevision.RevisionId == request.RevisionId
            && SelectedDefinitionId == request.DefinitionId,
        async () =>
        {
            if (await Apply(request.Intent))
            {
                Status = Text["InspectorEditApplied"];
            }
        });
}
