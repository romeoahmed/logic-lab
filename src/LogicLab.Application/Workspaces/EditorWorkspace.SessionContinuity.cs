namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceCommandOutcome> QueueContextualSessionAsync(
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
            return await AwaitReplayAsync(replayCompletion, cancellationToken)
                .ConfigureAwait(false);
        }

        var completed = await workCoordinator.RunSessionAsync(
            token => ExecuteReservedSessionCommandAsync(
                state,
                command,
                publication!,
                token),
            cancellationToken).ConfigureAwait(false);
        CompletePendingIdempotency(state, publication!, completed);
        return await publication!.PendingIntent.Completion.Task.ConfigureAwait(false);
    }

    private async Task<WorkspaceCommandOutcome> QueueRunPauseAsync(
        WorkspaceState state,
        PauseRun command,
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
                    if (cancellationToken.IsCancellationRequested)
                    {
                        var rejected = Reject(
                            WorkspaceOutcomeReasons.WorkspaceCancelled);
                        RecordIdempotencyUnderLock(
                            state,
                            command.Context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            rejected);
                        return rejected;
                    }

                    if (!MatchesRunControlPrecondition(state, command.Precondition))
                    {
                        var rejected = Reject(
                            WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
                        RecordIdempotencyUnderLock(
                            state,
                            command.Context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            rejected);
                        return rejected;
                    }

                    if (state.PendingRunPause is not null)
                    {
                        var rejected = Reject(
                            WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
                        RecordIdempotencyUnderLock(
                            state,
                            command.Context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            rejected);
                        return rejected;
                    }

                    publication = ReserveContextualIntentUnderLock(
                        state,
                        command,
                        accepted.CanonicalIdentity);
                    state.PendingRunPause = new RunPauseRequest(
                        publication,
                        command.Precondition.RunGeneration);
                    break;
            }
        }

        if (replayCompletion is not null)
        {
            return await AwaitReplayAsync(replayCompletion, cancellationToken)
                .ConfigureAwait(false);
        }

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
        return RejectIfRunRequiresPause(state, command)
            ?? command switch
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
                StartRun start => StartRunWithPrecondition(
                    state,
                    start),
                HotSwapSession hotSwap => HotSwapWithPrecondition(
                    state,
                    hotSwap,
                    cancellationToken),
                _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
            };
    }
}
