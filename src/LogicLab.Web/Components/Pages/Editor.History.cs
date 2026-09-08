using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private bool CanUndo => CanMutateWorkspace && Projection?.History.CanUndo is true;

    private bool CanRedo => CanMutateWorkspace && Projection?.History.CanRedo is true;

    private async Task MoveHistoryAsync(bool undo)
    {
        var precondition = new AuthoringPrecondition(Projection!.ProjectRevision.RevisionId);
        var outcome = await Execute(context => undo
            ? new Undo(context, precondition)
            : new Redo(context, precondition));
        if (Projection is null)
        {
            return;
        }

        Status = outcome is AuthoringCommitted
            ? Text[undo ? "UndoSucceeded" : "RedoSucceeded"]
            : Text["AuthoringRejected", ((WorkspaceCommandRejected)outcome).Code];
    }
}
