using LogicLab.Application.Work;

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
        WorkCoordinator.ScheduledSessionWork? scheduledWork = null;
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
                    if (workCoordinator.TryScheduleSession(
                            token => ExecuteReservedSessionCommandAsync(
                                state,
                                command,
                                publication,
                                token),
                            cancellationToken,
                            out scheduledWork,
                            out var schedulingRejection))
                    {
                        publication.PendingIntent.ScheduledSessionWork = scheduledWork;
                    }
                    else
                    {
                        CompletePendingIdempotencyUnderLock(
                            state,
                            publication,
                            Reject(schedulingRejection!));
                    }

                    break;
            }
        }

        if (replayCompletion is not null)
        {
            return await AwaitReplayAsync(replayCompletion, cancellationToken)
                .ConfigureAwait(false);
        }

        var pendingCompletion = publication!.PendingIntent.Completion.Task;
        if (scheduledWork is null)
        {
            return await pendingCompletion.ConfigureAwait(false);
        }

        using var cancellationRegistration = cancellationToken.UnsafeRegister(
            static work => ((WorkCoordinator.ScheduledSessionWork)work!).Cancel(),
            scheduledWork);
        if (await Task.WhenAny(scheduledWork.Completion, pendingCompletion)
                .ConfigureAwait(false)
            == scheduledWork.Completion)
        {
            var completed = await scheduledWork.Completion.ConfigureAwait(false);
            CompletePendingIdempotency(state, publication, completed);
        }

        return await pendingCompletion.ConfigureAwait(false);
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
            else if (GetReservedSessionAccessRejection(
                    state,
                    publication.Context) is { } rejection)
            {
                completed = Reject(rejection);
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

    private static string? GetReservedSessionAccessRejection(
        WorkspaceState state,
        WorkspaceCommandContext context)
    {
        lock (state.ContinuityGate)
        {
            return GetDurableAccessRejectionUnderLock(state, context.Caller)
                ?? (HasCurrentAttachmentUnderLock(state, context)
                    ? null
                    : WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
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
