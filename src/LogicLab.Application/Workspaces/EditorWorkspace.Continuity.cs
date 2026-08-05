using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

            lock (state.ContinuityGate)
            {
                if (request is InitialAttach)
                {
                    if (state.AttachmentId is not null)
                    {
                        return new AttachRejected(
                            WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                    }

                    return PublishAttachmentUnderLock(state, generation: 1);
                }

                var reattach = (Reattach)request;
                if (state.AttachmentId != reattach.PriorAttachmentId
                    || state.AttachmentGeneration != reattach.PriorGeneration)
                {
                    return new AttachRejected(
                        WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                return PublishAttachmentUnderLock(
                    state,
                    checked(state.AttachmentGeneration + 1));
            }
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

            lock (state.ContinuityGate)
            {
                if (!state.IsAttached
                    || state.AttachmentId != request.AttachmentId
                    || state.AttachmentGeneration != request.AttachmentGeneration)
                {
                    return new DetachRejected(
                        WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                state.IsAttached = false;
                lock (gate)
                {
                    state.DetachedAtTimestamp = timeProvider.GetTimestamp();
                }

                return new Detached(state.Id, state.AttachmentGeneration);
            }
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private Attached PublishAttachmentUnderLock(WorkspaceState state, ulong generation)
    {
        foreach (var pending in state.PendingIntents.Values)
        {
            pending.Completion.TrySetResult(
                Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment));
        }

        state.AttachmentId = WorkspaceAttachmentId.Create();
        state.AttachmentGeneration = generation;
        state.IsAttached = true;
        lock (gate)
        {
            state.DetachedAtTimestamp = null;
            state.LastAccessTimestamp = timeProvider.GetTimestamp();
        }

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

            lock (source.ContinuityGate)
            {
                if (!source.IsAttached
                    || source.AttachmentId != request.SourceAttachmentId
                    || source.AttachmentGeneration != request.SourceAttachmentGeneration)
                {
                    return new WorkspaceOpenRejected(
                        WorkspaceOutcomeReasons.StaleWorkspaceAttachment,
                        []);
                }

                TouchWorkspace(source);
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
                    timeProvider.GetTimestamp());
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
                return command is CloseWorkspace
                    ? new WorkspaceClosed(command.WorkspaceId)
                    : Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            lock (state.ContinuityGate)
            {
                var context = command.Context;
                switch (InspectContextualIntentUnderLock(state, command))
                {
                    case ContextualIntentTerminal terminal:
                        return terminal.Outcome;
                    case ContextualIntentReplay replay:
                        pendingCompletion = replay.Completion;
                        break;
                    case ContextualIntentAccepted accepted:
                        completed = command switch
                        {
                            ApplyEdit apply => ApplyWithPrecondition(
                                state,
                                apply,
                                cancellationToken),
                            Undo undo => MoveHistory(state, undo.Precondition, offset: -1),
                            Redo redo => MoveHistory(state, redo.Precondition, offset: 1),
                            CloseWorkspace => Close(state, cancellationToken),
                            _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
                        };
                        RecordIdempotencyUnderLock(
                            state,
                            context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            completed);
                        break;
                }
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

    private static bool HasCurrentAttachmentUnderLock(
        WorkspaceState state,
        WorkspaceCommandContext context)
    {
        return state.IsAttached
            && state.AttachmentId == context.AttachmentId
            && state.AttachmentGeneration == context.AttachmentGeneration;
    }

    private static bool HasCurrentAttachmentUnderLock(
        WorkspaceState state,
        WorkspaceQueryContext context)
    {
        return state.IsAttached
            && state.AttachmentId == context.AttachmentId
            && state.AttachmentGeneration == context.AttachmentGeneration;
    }

    private ContextualIntentInspection InspectContextualIntentUnderLock(
        WorkspaceState state,
        WorkspaceCommand command)
    {
        var context = command.Context;
        if (!HasCurrentAttachmentUnderLock(state, context))
        {
            return new ContextualIntentTerminal(
                Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment));
        }

        TouchWorkspace(state);

        var identity = CanonicalIdentity(command);
        if (state.IdempotencyRecords.TryGetValue(
            context.ClientIntentId,
            out var retained))
        {
            return new ContextualIntentTerminal(string.Equals(
                retained.CanonicalIdentity,
                identity,
                StringComparison.Ordinal)
                ? retained.Outcome
                : Reject(WorkspaceOutcomeReasons.IdempotencyKeyConflict));
        }

        if (state.PendingIntents.TryGetValue(
            context.ClientIntentId,
            out var pending))
        {
            return string.Equals(
                pending.CanonicalIdentity,
                identity,
                StringComparison.Ordinal)
                ? new ContextualIntentReplay(pending.Completion.Task)
                : new ContextualIntentTerminal(
                    Reject(WorkspaceOutcomeReasons.IdempotencyKeyConflict));
        }

        return state.IsIdempotencyWindowClosed
            ? new ContextualIntentTerminal(
                Reject(WorkspaceOutcomeReasons.IdempotencyWindowExpired))
            : new ContextualIntentAccepted(identity);
    }

    private static ContextualCommandPublication ReserveContextualIntentUnderLock(
        WorkspaceState state,
        WorkspaceCommand command,
        string canonicalIdentity)
    {
        var pending = new PendingIntent(
            canonicalIdentity,
            new TaskCompletionSource<WorkspaceCommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously));
        var publication = new ContextualCommandPublication(
            command.Context,
            canonicalIdentity,
            pending);
        state.PendingIntents.Add(command.Context.ClientIntentId, pending);
        return publication;
    }

    private WorkspaceCommandOutcome ApplyWithPrecondition(
        WorkspaceState state,
        ApplyEdit command,
        CancellationToken cancellationToken)
    {
        if (command.Precondition.ProjectRevisionId != state.Revision.RevisionId)
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
        if (state.Artifact?.Key != command.Precondition.CompilationArtifactKey)
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
        if (!MatchesSessionPrecondition(state, command.Precondition))
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
        if (!MatchesSessionPrecondition(state, command.Precondition))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return Step(state, cancellationToken);
    }

    private static bool MatchesSessionPrecondition(
        WorkspaceState state,
        SessionMutationPrecondition precondition)
    {
        return state.ActiveSession?.Artifact.Key == precondition.CompilationArtifactKey
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

    private void RecordIdempotencyUnderLock(
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
        lock (state.ContinuityGate)
        {
            var clientIntentId = publication.Context.ClientIntentId;
            if (!state.PendingIntents.TryGetValue(clientIntentId, out var pending)
                || !ReferenceEquals(pending, publication.PendingIntent))
            {
                return;
            }

            state.PendingIntents.Remove(clientIntentId);

            if (HasCurrentAttachmentUnderLock(state, publication.Context))
            {
                RecordIdempotencyUnderLock(
                    state,
                    clientIntentId,
                    publication.CanonicalIdentity,
                    outcome);
            }

            pending.Completion.TrySetResult(outcome);
        }
    }

    private static string CanonicalIdentity(WorkspaceCommand command)
    {
        return command switch
        {
            ApplyEdit apply => string.Concat(
                nameof(ApplyEdit),
                '|',
                apply.Precondition.ProjectRevisionId.Value,
                '|',
                JsonSerializer.Serialize<EditIntent>(
                    apply.Intent,
                    CanonicalJsonOptions)),
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
                request.Precondition.ProjectRevisionId.Value,
                '|',
                request.Precondition.EntryCircuitDefinitionId.Value,
                '|',
                request.Precondition.LibrarySnapshotFingerprint),
            CreateSession create => string.Concat(
                nameof(CreateSession),
                '|',
                JsonSerializer.Serialize(
                    create.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions)),
            ScheduleInputStimulus schedule => string.Concat(
                nameof(ScheduleInputStimulus),
                '|',
                schedule.Precondition.SessionId.Value,
                '|',
                schedule.Precondition.SessionVersion,
                '|',
                JsonSerializer.Serialize(
                    schedule.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions),
                '|',
                schedule.LogicalTime,
                '|',
                JsonSerializer.Serialize(
                    schedule.Assignments,
                    CanonicalJsonOptions)),
            StepSession step => string.Concat(
                nameof(StepSession),
                '|',
                step.Precondition.SessionId.Value,
                '|',
                step.Precondition.SessionVersion,
                '|',
                JsonSerializer.Serialize(
                    step.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions)),
            CloseWorkspace => nameof(CloseWorkspace),
            _ => command.GetType().FullName ?? command.GetType().Name,
        };
    }

    private static JsonSerializerOptions CanonicalJsonOptions { get; } = new()
    {
        TypeInfoResolver = new DomainPolymorphicTypeResolver(),
    };

    private sealed class DomainPolymorphicTypeResolver : DefaultJsonTypeInfoResolver
    {
        private static readonly Type[] ConcreteDomainTypes =
        [
            .. typeof(EditIntent).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && !type.IsInterface)
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

        public override JsonTypeInfo GetTypeInfo(
            Type type,
            JsonSerializerOptions options)
        {
            var typeInfo = base.GetTypeInfo(type, options);
            if (!type.IsAbstract || type.Assembly != typeof(EditIntent).Assembly)
            {
                return typeInfo;
            }

            var derivedTypes = ConcreteDomainTypes
                .Where(candidate => candidate.IsAssignableTo(type))
                .ToArray();
            if (derivedTypes.Length == 0)
            {
                return typeInfo;
            }

            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
            };
            foreach (var derivedType in derivedTypes)
            {
                typeInfo.PolymorphismOptions.DerivedTypes.Add(
                    new JsonDerivedType(derivedType, derivedType.FullName!));
            }

            return typeInfo;
        }
    }

    private sealed record ContextualCommandPublication(
        WorkspaceCommandContext Context,
        string CanonicalIdentity,
        PendingIntent PendingIntent);

    private abstract record ContextualIntentInspection;

    private sealed record ContextualIntentTerminal(WorkspaceCommandOutcome Outcome)
        : ContextualIntentInspection;

    private sealed record ContextualIntentReplay(Task<WorkspaceCommandOutcome> Completion)
        : ContextualIntentInspection;

    private sealed record ContextualIntentAccepted(string CanonicalIdentity)
        : ContextualIntentInspection;
}
