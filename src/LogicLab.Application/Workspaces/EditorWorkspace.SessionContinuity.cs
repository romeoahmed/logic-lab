using LogicLab.Application.Work;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceCommandOutcome> QueueContextualSessionAsync(
        WorkspaceState state,
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ContextualIntentReplay? replayIntent = null;
        PendingIntent? pendingIntent = null;
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
                    replayIntent = replay;
                    break;
                case ContextualIntentAccepted accepted:
                    pendingIntent = ReserveContextualIntentUnderLock(
                        state,
                        command,
                        accepted.CanonicalIdentity);
                    if (workCoordinator.TryScheduleSession(
                            state.Id,
                            command.Context.Caller,
                            token => ExecuteReservedSessionCommandAsync(
                                state,
                                command,
                                pendingIntent,
                                token),
                            cancellationToken,
                            out scheduledWork,
                            out var schedulingRejection))
                    {
                        pendingIntent.ScheduledSessionWork = scheduledWork;
                    }
                    else
                    {
                        CompletePendingIdempotencyUnderLock(
                            state,
                            pendingIntent,
                            Reject(
                                schedulingRejection!.Code,
                                policyEvidence: schedulingRejection.PolicyEvidence));
                    }

                    break;
            }
        }

        if (replayIntent is not null)
        {
            return await AwaitReplayAsync(state, replayIntent, cancellationToken)
                .ConfigureAwait(false);
        }

        var pendingCompletion = pendingIntent!.Completion.Task;
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
            CompletePendingIdempotency(state, pendingIntent, completed);
        }

        return await pendingCompletion.ConfigureAwait(false);
    }

    private async Task<WorkspaceCommandOutcome> QueueRunPauseAsync(
        WorkspaceState state,
        PauseRun command,
        CancellationToken cancellationToken)
    {
        ContextualIntentReplay? replayIntent = null;
        PendingIntent? pendingIntent = null;
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
                    replayIntent = replay;
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
                            WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
                        RecordIdempotencyUnderLock(
                            state,
                            command.Context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            rejected);
                        return rejected;
                    }

                    pendingIntent = ReserveContextualIntentUnderLock(
                        state,
                        command,
                        accepted.CanonicalIdentity);
                    state.PendingRunPause = new RunPauseRequest(
                        pendingIntent,
                        command.Precondition.RunGeneration);
                    break;
            }
        }

        if (replayIntent is not null)
        {
            return await AwaitReplayAsync(state, replayIntent, cancellationToken)
                .ConfigureAwait(false);
        }

        return await pendingIntent!.Completion.Task.ConfigureAwait(false);
    }

    private async ValueTask<WorkspaceCommandOutcome> ExecuteReservedSessionCommandAsync(
        WorkspaceState state,
        WorkspaceCommand command,
        PendingIntent pendingIntent,
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
                    pendingIntent.Context) is { } rejection)
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

            CompletePendingIdempotency(state, pendingIntent, completed);
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
