using System.Text.Json;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    public async Task<WorkspaceAttachOutcome> AttachAsync(
        AttachRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return new AttachRejected(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        var acquisition = AcquireWorkspace(request.WorkspaceId);
        if (acquisition.Lease is null)
        {
            return string.Equals(
                acquisition.RejectionReason,
                WorkspaceOutcomeReasons.WorkspaceExpired,
                StringComparison.Ordinal)
                ? new Expired(WorkspaceOutcomeReasons.WorkspaceExpired)
                : new AttachRejected(acquisition.RejectionReason!);
        }

        using var lease = acquisition.Lease;
        var state = lease.State;
        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return new AttachRejected(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        try
        {
            if (state.IsRetired)
            {
                return new AttachRejected(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            if (!string.Equals(
                request.BuildFingerprint,
                buildFingerprint,
                StringComparison.Ordinal))
            {
                return new AttachRejected(WorkspaceOutcomeReasons.BuildFingerprintMismatch);
            }

            if (request is InitialAttach)
            {
                if (state.AttachmentId is not null)
                {
                    return new AttachRejected(
                        WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                return PublishAttachment(state, generation: 1);
            }

            var reattach = (Reattach)request;
            if (state.AttachmentId != reattach.PriorAttachmentId
                || state.AttachmentGeneration != reattach.PriorGeneration)
            {
                return new AttachRejected(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            }

            return PublishAttachment(state, checked(state.AttachmentGeneration + 1));
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    public async Task<WorkspaceDetachOutcome> DetachAsync(
        DetachRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return new DetachRejected(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        var acquisition = AcquireWorkspace(request.WorkspaceId);
        if (acquisition.Lease is null)
        {
            return new DetachRejected(acquisition.RejectionReason!);
        }

        using var lease = acquisition.Lease;
        var state = lease.State;
        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return new DetachRejected(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        try
        {
            if (state.IsRetired)
            {
                return new DetachRejected(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            if (!state.IsAttached
                || state.AttachmentId != request.AttachmentId
                || state.AttachmentGeneration != request.AttachmentGeneration)
            {
                return new DetachRejected(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            }

            state.IsAttached = false;
            state.DetachedAtUtc = timeProvider.GetUtcNow();
            return new Detached(state.Id, state.AttachmentGeneration);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static Attached PublishAttachment(WorkspaceState state, ulong generation)
    {
        foreach (var pending in state.PendingIntents.Values)
        {
            pending.Completion.TrySetResult(
                Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment));
        }

        state.AttachmentId = WorkspaceAttachmentId.Create();
        state.AttachmentGeneration = generation;
        state.IsAttached = true;
        state.DetachedAtUtc = null;
        state.IdempotencyRecords.Clear();
        state.PendingIntents.Clear();
        state.IdempotencyOrder.Clear();
        state.IsIdempotencyWindowClosed = false;
        return new Attached(state.AttachmentId, generation, Project(state));
    }

    private async Task<WorkspaceOpenOutcome> CopyAsync(
        CopyWorkspace request,
        CancellationToken cancellationToken)
    {
        var acquisition = AcquireWorkspace(request.SourceWorkspaceId);
        if (acquisition.Lease is null)
        {
            return new WorkspaceOpenRejected(acquisition.RejectionReason!, []);
        }

        using var lease = acquisition.Lease;
        var source = lease.State;
        try
        {
            await source.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return new WorkspaceOpenRejected(
                WorkspaceOutcomeReasons.WorkspaceCancelled,
                []);
        }

        try
        {
            if (source.IsRetired)
            {
                return new WorkspaceOpenRejected(
                    WorkspaceOutcomeReasons.WorkspaceNotFound,
                    []);
            }

            if (!source.IsAttached
                || source.AttachmentId != request.SourceAttachmentId
                || source.AttachmentGeneration != request.SourceAttachmentGeneration)
            {
                return new WorkspaceOpenRejected(
                    WorkspaceOutcomeReasons.StaleWorkspaceAttachment,
                    []);
            }

            if (source.ProjectionVersion != request.ExpectedProjectionVersion)
            {
                return new WorkspaceOpenRejected(
                    WorkspaceOutcomeReasons.ProjectionVersionPreconditionFailed,
                    []);
            }

            var rejectionReason = ReserveWorkspace(out var retired);
            RetireAll(retired);
            if (rejectionReason is not null)
            {
                return new WorkspaceOpenRejected(rejectionReason, []);
            }

            var hasReservation = true;
            try
            {
                var id = WorkspaceId.Create();
                var copy = new WorkspaceState(
                    id,
                    source.Revision,
                    timeProvider.GetUtcNow());
                lock (gate)
                {
                    workspaceReservations--;
                    hasReservation = false;
                    if (isDisposed || cancellationToken.IsCancellationRequested)
                    {
                        rejectionReason = WorkspaceOutcomeReasons.WorkspaceCancelled;
                    }
                    else
                    {
                        workspaces.Add(id, copy);
                    }
                }

                if (rejectionReason is not null)
                {
                    copy.CommandGate.Dispose();
                    return new WorkspaceOpenRejected(rejectionReason, []);
                }

                return new WorkspaceOpened(id, Project(copy));
            }
            finally
            {
                if (hasReservation)
                {
                    ReleaseWorkspaceReservation();
                }
            }
        }
        finally
        {
            source.CommandGate.Release();
        }
    }

    private async ValueTask<WorkspaceCommandOutcome> ExecuteContextualCommandAsync(
        WorkspaceState state,
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        Task<WorkspaceCommandOutcome>? pendingCompletion = null;
        WorkspaceCommandOutcome? completed = null;
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
                return Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var context = command.Context!;
            if (!HasCurrentAttachment(state, context))
            {
                return Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            }

            var identity = CanonicalIdentity(command);
            if (state.IdempotencyRecords.TryGetValue(
                context.ClientIntentId,
                out var retained))
            {
                return string.Equals(
                    retained.CanonicalIdentity,
                    identity,
                    StringComparison.Ordinal)
                    ? retained.Outcome
                    : Reject(WorkspaceOutcomeReasons.IdempotencyKeyConflict);
            }

            if (state.PendingIntents.TryGetValue(
                context.ClientIntentId,
                out var pending))
            {
                if (!string.Equals(
                    pending.CanonicalIdentity,
                    identity,
                    StringComparison.Ordinal))
                {
                    return Reject(WorkspaceOutcomeReasons.IdempotencyKeyConflict);
                }

                pendingCompletion = pending.Completion.Task;
            }
            else
            {
                if (state.IsIdempotencyWindowClosed)
                {
                    return Reject(WorkspaceOutcomeReasons.IdempotencyWindowExpired);
                }

                completed = command switch
                {
                    ApplyEdit apply => ApplyWithPrecondition(
                        state,
                        apply,
                        cancellationToken),
                    Undo undo => MoveHistory(state, undo.Precondition, offset: -1),
                    Redo redo => MoveHistory(state, redo.Precondition, offset: 1),
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
                    CloseWorkspace => Close(state, cancellationToken),
                    _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
                };
                RecordIdempotency(
                    state,
                    context.ClientIntentId,
                    identity,
                    completed);
            }
        }
        finally
        {
            state.CommandGate.Release();
        }

        return pendingCompletion is null
            ? completed!
            : await pendingCompletion.ConfigureAwait(false);
    }

    private static bool HasCurrentAttachment(
        WorkspaceState state,
        WorkspaceCommandContext context)
    {
        return state.IsAttached
            && state.AttachmentId == context.AttachmentId
            && state.AttachmentGeneration == context.AttachmentGeneration;
    }

    private WorkspaceCommandOutcome ApplyWithPrecondition(
        WorkspaceState state,
        ApplyEdit command,
        CancellationToken cancellationToken)
    {
        if (command.Precondition!.ProjectRevisionId != state.Revision.RevisionId)
        {
            return Reject(WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
        }

        return Apply(state, command, cancellationToken);
    }

    private static WorkspaceCommandOutcome MoveHistory(
        WorkspaceState state,
        AuthoringPrecondition precondition,
        int offset)
    {
        if (precondition.ProjectRevisionId != state.Revision.RevisionId)
        {
            return Reject(WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
        }

        var target = state.HistoryCursor + offset;
        if (target < 0 || target >= state.History.Count)
        {
            return Reject(WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
        }

        state.HistoryCursor = target;
        state.Revision = state.History[target];
        state.Artifact = null;
        state.Compilation = NotRequestedCompilation();
        state.ProjectionVersion++;
        return new AuthoringCommitted(
            state.Revision.RevisionId,
            state.ProjectionVersion);
    }

    private WorkspaceCommandOutcome OpenSessionWithPrecondition(
        WorkspaceState state,
        CreateSession command,
        CancellationToken cancellationToken)
    {
        if (state.Artifact?.Key != command.Precondition!.CompilationArtifactKey)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return OpenSession(state, cancellationToken);
    }

    private WorkspaceCommandOutcome ScheduleWithPrecondition(
        WorkspaceState state,
        ScheduleInputStimulus command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition!))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return Schedule(state, command, cancellationToken);
    }

    private WorkspaceCommandOutcome StepWithPrecondition(
        WorkspaceState state,
        StepSession command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition!))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return Step(state, cancellationToken);
    }

    private static bool MatchesSessionPrecondition(
        WorkspaceState state,
        SessionMutationPrecondition precondition)
    {
        return state.Artifact?.Key == precondition.CompilationArtifactKey
            && state.Simulation is { } simulation
            && simulation.SessionId == precondition.SessionId
            && simulation.SessionVersion == precondition.SessionVersion;
    }

    private AuthoringCommitted CommitAuthoringRevision(
        WorkspaceState state,
        ProjectRevision revision)
    {
        if (state.HistoryCursor < state.History.Count - 1)
        {
            state.History.RemoveRange(
                state.HistoryCursor + 1,
                state.History.Count - state.HistoryCursor - 1);
        }

        state.History.Add(revision);
        state.HistoryCursor++;
        var excess = state.History.Count - workspacePolicy.HistoryRevisionCount;
        if (excess > 0)
        {
            state.History.RemoveRange(0, excess);
            state.HistoryCursor -= excess;
        }

        state.Revision = revision;
        state.Artifact = null;
        state.Compilation = NotRequestedCompilation();
        state.ProjectionVersion++;
        return new AuthoringCommitted(
            state.Revision.RevisionId,
            state.ProjectionVersion);
    }

    private void RecordIdempotency(
        WorkspaceState state,
        ClientIntentId clientIntentId,
        string identity,
        WorkspaceCommandOutcome outcome)
    {
        state.IdempotencyRecords.Add(
            clientIntentId,
            new IdempotencyRecord(identity, outcome));
        state.IdempotencyOrder.Enqueue(clientIntentId);
        while (state.IdempotencyRecords.Count > workspacePolicy.IdempotencyRecordCount)
        {
            var expired = state.IdempotencyOrder.Dequeue();
            state.IdempotencyRecords.Remove(expired);
            state.IsIdempotencyWindowClosed = true;
        }
    }

    private void CompletePendingIdempotency(
        WorkspaceState state,
        ContextualCommandPublication publication,
        WorkspaceCommandOutcome outcome)
    {
        var clientIntentId = publication.Context.ClientIntentId;
        if (!state.PendingIntents.Remove(
            clientIntentId,
            out var pending))
        {
            return;
        }

        if (HasCurrentAttachment(state, publication.Context))
        {
            RecordIdempotency(
                state,
                clientIntentId,
                publication.CanonicalIdentity,
                outcome);
        }

        pending.Completion.TrySetResult(outcome);
    }

    private static string CanonicalIdentity(WorkspaceCommand command)
    {
        // Supplying the closed runtime type includes the derived intent's public shape.
        // Source: https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/polymorphism
        return command switch
        {
            ApplyEdit apply => string.Concat(
                nameof(ApplyEdit),
                '|',
                apply.Precondition!.ProjectRevisionId.Value,
                '|',
                apply.Intent.GetType().FullName,
                '|',
                JsonSerializer.Serialize(apply.Intent, apply.Intent.GetType())),
            Undo undo => string.Concat(
                nameof(Undo),
                '|',
                undo.Precondition.ProjectRevisionId.Value),
            Redo redo => string.Concat(
                nameof(Redo),
                '|',
                redo.Precondition.ProjectRevisionId.Value),
            RequestCompilation request => string.Concat(
                nameof(RequestCompilation),
                '|',
                request.Precondition!.ProjectRevisionId.Value,
                '|',
                request.Precondition.EntryCircuitDefinitionId.Value,
                '|',
                request.Precondition.LibrarySnapshotFingerprint),
            CreateSession create => string.Concat(
                nameof(CreateSession),
                '|',
                JsonSerializer.Serialize(
                    create.Precondition!.CompilationArtifactKey)),
            ScheduleInputStimulus schedule => string.Concat(
                nameof(ScheduleInputStimulus),
                '|',
                schedule.Precondition!.SessionId.Value,
                '|',
                schedule.Precondition.SessionVersion,
                '|',
                JsonSerializer.Serialize(
                    schedule.Precondition.CompilationArtifactKey),
                '|',
                schedule.LogicalTime,
                '|',
                JsonSerializer.Serialize(schedule.Assignments)),
            StepSession step => string.Concat(
                nameof(StepSession),
                '|',
                step.Precondition!.SessionId.Value,
                '|',
                step.Precondition.SessionVersion,
                '|',
                JsonSerializer.Serialize(
                    step.Precondition.CompilationArtifactKey)),
            CloseWorkspace => nameof(CloseWorkspace),
            _ => command.GetType().FullName ?? command.GetType().Name,
        };
    }

    private sealed record ContextualCommandPublication(
        WorkspaceCommandContext Context,
        string CanonicalIdentity);
}
