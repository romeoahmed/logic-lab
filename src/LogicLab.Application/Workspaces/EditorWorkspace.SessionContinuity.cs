namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceCommandOutcome> QueueContextualSessionAsync(
        WorkspaceId workspaceId,
        WorkspaceState state,
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        Task<WorkspaceCommandOutcome>? replayCompletion = null;
        ContextualCommandPublication? publication = null;
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        lock (state.ContinuityGate)
        {
            switch (InspectContextualIntentUnderLock(state, command))
            {
                case ContextualIntentTerminal terminal:
                    return terminal.Outcome;
                case ContextualIntentReplay replay:
                    replayCompletion = replay.Completion;
                    break;
                case ContextualIntentAccepted accepted:
                    publication = ReserveContextualIntentUnderLock(
                        state,
                        command,
                        accepted.CanonicalIdentity);
                    break;
            }
        }

        if (replayCompletion is not null)
        {
            return await replayCompletion.ConfigureAwait(false);
        }

        var completed = await workCoordinator.RunSessionAsync(
            workspaceId,
            token => ExecuteReservedSessionCommandAsync(
                state,
                command,
                publication!,
                token),
            cancellationToken).ConfigureAwait(false);
        CompletePendingIdempotency(state, publication!, completed);
        return await publication!.PendingIntent.Completion.Task.ConfigureAwait(false);
    }

    private async ValueTask<WorkspaceCommandOutcome> ExecuteReservedSessionCommandAsync(
        WorkspaceState state,
        WorkspaceCommand command,
        ContextualCommandPublication publication,
        CancellationToken cancellationToken)
    {
        WorkspaceCommandOutcome completed;
        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        try
        {
            if (state.IsRetired)
            {
                completed = Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }
            else if (!HasCurrentAttachmentSafely(state, publication.Context))
            {
                completed = Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            }
            else
            {
                completed = ExecuteSessionCommandWithPrecondition(
                    state,
                    command,
                    cancellationToken);
            }

            CompletePendingIdempotency(state, publication, completed);
            return completed;
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private WorkspaceCommandOutcome ExecuteSessionCommandWithPrecondition(
        WorkspaceState state,
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        return command switch
        {
            CreateSession create => OpenSessionWithPrecondition(
                state,
                create,
                cancellationToken),
            ScheduleInputStimulus schedule => ScheduleWithPrecondition(
                state,
                schedule,
                cancellationToken),
            StepSession step => StepWithPrecondition(
                state,
                step,
                cancellationToken),
            _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
        };
    }
}
