using System.Diagnostics;
using System.Globalization;
using System.Text;
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
        using var operation = TryEnterOperation();
        if (operation is null)
        {
            return RejectAttach(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return RejectAttach(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        using var acquisition = AcquireWorkspace(request.WorkspaceId);
        if (!acquisition.IsAcquired)
        {
            return CreateUnavailableAttachOutcome(
                request,
                acquisition.RejectionReason);
        }

        var state = acquisition.State;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            request.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return CreateUnavailableAttachOutcome(
                request,
                admissionRejection);
        }

        try
        {
            if (!IsCurrentWorkspace(state))
            {
                return CreateUnavailableAttachOutcome(
                    request,
                    WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var authorizationRejection = GetDurableAccessRejection(
                state,
                request.Caller);
            if (authorizationRejection is not null)
            {
                return CreateUnavailableAttachOutcome(
                    request,
                    authorizationRejection);
            }

            if (request is RecoverAttach
                && state.Durability is not DurableWorkspaceState)
            {
                return RejectAttach(
                    WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            }

            if (!string.Equals(
                request.BuildFingerprint,
                buildFingerprint,
                StringComparison.Ordinal))
            {
                return RejectAttach(WorkspaceOutcomeReasons.BuildFingerprintMismatch);
            }

            lock (state.ContinuityGate)
            {
                return request switch
                {
                    InitialAttach when state.AttachmentId is not null =>
                        RejectAttach(WorkspaceOutcomeReasons.StaleWorkspaceAttachment),
                    InitialAttach => PublishAttachmentUnderLock(
                            state,
                            request,
                            generation: 1),
                    Reattach reattach
                        when state.AttachmentId != reattach.PriorAttachmentId
                            || state.AttachmentGeneration != reattach.PriorGeneration =>
                        RejectAttach(WorkspaceOutcomeReasons.StaleWorkspaceAttachment),
                    Reattach => PublishAttachmentUnderLock(
                            state,
                            request,
                            checked(state.AttachmentGeneration + 1)),
                    RecoverAttach when state.AttachmentId is null =>
                        RejectAttach(WorkspaceOutcomeReasons.StaleWorkspaceAttachment),
                    RecoverAttach => PublishAttachmentUnderLock(
                            state,
                            request,
                            checked(state.AttachmentGeneration + 1)),
                    _ => throw new UnreachableException(),
                };
            }
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static WorkspaceAttachOutcome CreateUnavailableAttachOutcome(
        AttachRequest request,
        string rejectionReason)
    {
        var expired = request is Reattach
            && string.Equals(
                rejectionReason,
                WorkspaceOutcomeReasons.WorkspaceNotFound,
                StringComparison.Ordinal);

        return expired
            ? new Expired(WorkspaceOutcomeReasons.WorkspaceExpired)
            : RejectAttach(rejectionReason);
    }

    public async Task<WorkspaceDetachOutcome> DetachAsync(
        DetachRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = TryEnterOperation();
        if (operation is null)
        {
            return new DetachRejected(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new DetachRejected(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        using var acquisition = AcquireWorkspace(request.WorkspaceId);
        if (!acquisition.IsAcquired)
        {
            return new DetachRejected(acquisition.RejectionReason);
        }

        var state = acquisition.State;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            request.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return new DetachRejected(admissionRejection);
        }

        try
        {
            if (state.IsRetired)
            {
                return new DetachRejected(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var authorizationRejection = GetDurableAccessRejection(
                state,
                request.Caller);
            if (authorizationRejection is not null)
            {
                return new DetachRejected(authorizationRejection);
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

                if (state.Simulation is
                    {
                        Run: RunRunningProjection
                        {
                            RunGeneration: var runGeneration,
                        },
                    } simulation)
                {
                    state.Simulation = WithRun(
                        simulation,
                        new RunPausedProjection(
                            runGeneration,
                            RunPauseReason.Detached));
                    state.ProjectionVersion++;
                    CompletePendingRunPauseUnderLock(
                        state,
                        runGeneration,
                        new RunPaused(
                            runGeneration,
                            simulation.SessionVersion,
                            simulation.LogicalTime,
                            RunPauseReason.Detached,
                            state.ProjectionVersion));
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

    private WorkspaceAttachOutcome PublishAttachmentUnderLock(
        WorkspaceState state,
        AttachRequest request,
        ulong generation)
    {
        lock (gate)
        {
            if (!IsCurrentWorkspaceUnderLock(state))
            {
                return CreateUnavailableAttachOutcome(
                    request,
                    WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            foreach (var pending in state.PendingIntents.Values)
            {
                CompletePendingWaitersUnderLock(
                    state,
                    pending,
                    Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment));
            }

            state.AttachmentId = WorkspaceAttachmentId.Create();
            state.AttachmentGeneration = generation;
            state.IsAttached = true;
            state.DetachedAtTimestamp = null;
            state.LastAccessTimestamp = timeProvider.GetTimestamp();

            state.IdempotencyRecords.Clear();
            state.PendingIntents.Clear();
            state.IdempotencyOrder.Clear();
            state.IsIdempotencyWindowClosed = false;
            state.PendingRunPause = null;
            return new Attached(state.AttachmentId, generation, Project(state));
        }
    }

    private async Task<WorkspaceOpenOutcome> CopyAsync(
        CopyWorkspace request,
        CancellationToken cancellationToken)
    {
        using var acquisition = AcquireWorkspace(request.SourceWorkspaceId);
        if (!acquisition.IsAcquired)
        {
            return RejectOpen(acquisition.RejectionReason);
        }

        var source = acquisition.State;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            source,
            request.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return RejectOpen(admissionRejection);
        }

        try
        {
            if (source.IsRetired)
            {
                return RejectOpen(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var authorizationRejection = GetDurableAccessRejection(
                source,
                request.Caller);
            if (authorizationRejection is not null)
            {
                return RejectOpen(authorizationRejection);
            }

            lock (source.ContinuityGate)
            {
                if (!source.IsAttached
                    || source.AttachmentId != request.SourceAttachmentId
                    || source.AttachmentGeneration != request.SourceAttachmentGeneration)
                {
                    return RejectOpen(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                TouchWorkspace(source);
            }

            if (source.ProjectionVersion != request.ExpectedProjectionVersion)
            {
                return RejectOpen(
                    WorkspaceOutcomeReasons.ProjectionVersionPreconditionFailed);
            }

            if (request.SaveTarget == WorkspaceCopySaveTarget.Preserve
                && source.Durability is PendingDurableClaimState)
            {
                return RejectOpen(WorkspaceOutcomeReasons.DurableClaimUnresolved);
            }

            var rejectionReason = ReserveWorkspace(
                request.Caller,
                out var retired,
                out var policyEvidence);
            RetireAll(retired);
            if (rejectionReason is not null)
            {
                return RejectOpen(rejectionReason, policyEvidence: policyEvidence);
            }

            var hasReservation = true;
            try
            {
                var id = WorkspaceId.Create();
                var copy = new WorkspaceState(
                    id,
                    source.Revision,
                    request.Caller,
                    timeProvider.GetTimestamp());
                if (request.SaveTarget == WorkspaceCopySaveTarget.Preserve
                    && source.Durability is DurableWorkspaceState durable)
                {
                    copy.Durability = durable.Copy();
                }
                lock (gate)
                {
                    hasReservation = false;
                    rejectionReason = PublishWorkspaceReservationUnderLock(
                        copy,
                        cancellationToken);
                }

                if (rejectionReason is not null)
                {
                    DisposeUnpublishedWorkspace(copy);
                    return RejectOpen(rejectionReason);
                }

                return new WorkspaceOpened(id, Project(copy));
            }
            finally
            {
                if (hasReservation)
                {
                    ReleaseWorkspaceReservation(request.Caller);
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
        ContextualIntentReplay? replayIntent = null;
        WorkspaceCommandOutcome? completed = null;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            command.Context.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return command is CloseWorkspace
                ? new WorkspaceClosed(command.WorkspaceId)
                : Reject(admissionRejection);
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
                        replayIntent = replay;
                        break;
                    case ContextualIntentAccepted accepted:
                        completed = RejectIfRunRequiresPause(state, command)
                            ?? command switch
                            {
                                ApplyEdit apply => ApplyWithPrecondition(
                                    state,
                                    apply,
                                    cancellationToken),
                                Undo undo => MoveHistory(
                                    state,
                                    undo.Precondition,
                                    offset: -1),
                                Redo redo => MoveHistory(
                                    state,
                                    redo.Precondition,
                                    offset: 1),
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

        return replayIntent is null
            ? completed!
            : await AwaitReplayAsync(state, replayIntent, cancellationToken)
                .ConfigureAwait(false);
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
        var authorizationRejection = GetDurableAccessRejectionUnderLock(
            state,
            context.Caller);
        if (authorizationRejection is not null)
        {
            return new ContextualIntentTerminal(command is CloseWorkspace
                ? new WorkspaceClosed(command.WorkspaceId)
                : Reject(authorizationRejection));
        }

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
            if (!string.Equals(
                    pending.CanonicalIdentity,
                    identity,
                    StringComparison.Ordinal))
            {
                return new ContextualIntentTerminal(
                    Reject(WorkspaceOutcomeReasons.IdempotencyKeyConflict));
            }

            var waiter = new PendingReplayWaiter(context);
            pending.ReplayWaiters.Add(waiter);
            return new ContextualIntentReplay(pending, waiter);
        }

        return state.IsIdempotencyWindowClosed && command is not CloseWorkspace
            ? new ContextualIntentTerminal(
                Reject(WorkspaceOutcomeReasons.IdempotencyWindowExpired))
            : new ContextualIntentAccepted(identity);
    }

    private static PendingIntent ReserveContextualIntentUnderLock(
        WorkspaceState state,
        WorkspaceCommand command,
        string canonicalIdentity)
    {
        var pending = new PendingIntent(
            command.Context,
            canonicalIdentity,
            new TaskCompletionSource<WorkspaceCommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously));
        state.PendingIntents.Add(command.Context.ClientIntentId, pending);
        return pending;
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
        state.Compilation = CompilationNotRequestedProjection.Instance;
        state.ProjectionVersion++;
        return new AuthoringCommitted(
            state.Revision.RevisionId,
            state.ProjectionVersion);
    }

    private static async Task<WorkspaceCommandOutcome> AwaitReplayAsync(
        WorkspaceState state,
        ContextualIntentReplay replay,
        CancellationToken cancellationToken)
    {
        try
        {
            return await replay.Waiter.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            lock (state.ContinuityGate)
            {
                var rejection = GetDurableAccessRejectionUnderLock(
                        state,
                        replay.Waiter.Context.Caller)
                    ?? (HasCurrentAttachmentUnderLock(state, replay.Waiter.Context)
                        ? null
                        : WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                return Reject(
                    rejection ?? WorkspaceOutcomeReasons.WorkspaceCancelled);
            }
        }
        finally
        {
            lock (state.ContinuityGate)
            {
                replay.PendingIntent.ReplayWaiters.Remove(replay.Waiter);
            }
        }
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
        state.Compilation = CompilationNotRequestedProjection.Instance;
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
        PendingIntent pendingIntent,
        WorkspaceCommandOutcome outcome)
    {
        lock (state.ContinuityGate)
        {
            CompletePendingIdempotencyUnderLock(state, pendingIntent, outcome);
        }
    }

    private void CompletePendingIdempotencyUnderLock(
        WorkspaceState state,
        PendingIntent pendingIntent,
        WorkspaceCommandOutcome outcome)
    {
        var clientIntentId = pendingIntent.Context.ClientIntentId;
        if (!state.PendingIntents.TryGetValue(clientIntentId, out var pending)
            || !ReferenceEquals(pending, pendingIntent))
        {
            return;
        }

        state.PendingIntents.Remove(clientIntentId);

        if (HasCurrentAttachmentUnderLock(state, pendingIntent.Context))
        {
            RecordIdempotencyUnderLock(
                state,
                clientIntentId,
                pendingIntent.CanonicalIdentity,
                outcome);
        }

        CompletePendingWaitersUnderLock(state, pending, outcome);
    }

    private static void CompletePendingWaitersUnderLock(
        WorkspaceState state,
        PendingIntent pending,
        WorkspaceCommandOutcome outcome)
    {
        pending.Completion.TrySetResult(outcome);
        foreach (var waiter in pending.ReplayWaiters)
        {
            var rejection = GetDurableAccessRejectionUnderLock(
                    state,
                    waiter.Context.Caller)
                ?? (HasCurrentAttachmentUnderLock(state, waiter.Context)
                    ? null
                    : WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            waiter.Completion.TrySetResult(
                rejection is null ? outcome : Reject(rejection));
        }

        pending.ReplayWaiters.Clear();
    }

    private static string CanonicalIdentity(WorkspaceCommand command)
    {
        return command switch
        {
            ApplyEdit apply => SerializeCanonicalIdentity(
                nameof(ApplyEdit),
                apply.Precondition.ProjectRevisionId.Value,
                JsonSerializer.Serialize<EditIntent>(
                    apply.Intent,
                    CanonicalJsonOptions)),
            Undo undo => SerializeCanonicalIdentity(
                nameof(Undo),
                undo.Precondition.ProjectRevisionId.Value),
            Redo redo => SerializeCanonicalIdentity(
                nameof(Redo),
                redo.Precondition.ProjectRevisionId.Value),
            RequestCompilation request => SerializeCanonicalIdentity(
                nameof(RequestCompilation),
                request.Precondition.ProjectRevisionId.Value,
                request.Precondition.EntryCircuitDefinitionId.Value,
                request.Precondition.LibrarySnapshotFingerprint),
            CreateSession create => SerializeCanonicalIdentity(
                nameof(CreateSession),
                JsonSerializer.Serialize(
                    create.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions),
                JsonSerializer.Serialize(create.Configuration, CanonicalJsonOptions)),
            RestartSession restart => SerializeCanonicalIdentity(
                nameof(RestartSession),
                JsonSerializer.Serialize(restart.Precondition, CanonicalJsonOptions),
                JsonSerializer.Serialize(restart.TargetCompilationArtifactKey, CanonicalJsonOptions),
                JsonSerializer.Serialize(restart.Configuration, CanonicalJsonOptions)),
            CloseSession close => SerializeCanonicalIdentity(
                nameof(CloseSession),
                JsonSerializer.Serialize(close.Precondition, CanonicalJsonOptions)),
            ScheduleStimulusBatch schedule => SerializeCanonicalIdentity(
                nameof(ScheduleStimulusBatch),
                schedule.Precondition.SessionId.Value,
                schedule.Precondition.SessionVersion.ToString(
                    CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(
                    schedule.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions),
                schedule.Batch.LogicalTime.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(
                    schedule.Batch.Assignments.Select(assignment => new
                    {
                        assignment.DriverSource,
                        // LogicVector exposes Width as a property; serialize its values explicitly.
                        Value = Values(assignment.Value),
                    }),
                    CanonicalJsonOptions)),
            StepSession step => SerializeCanonicalIdentity(
                nameof(StepSession),
                step.Precondition.SessionId.Value,
                step.Precondition.SessionVersion.ToString(
                    CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(
                    step.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions)),
            ReplaceProbes replace => CanonicalReplaceProbesIdentity(replace),
            StartRun start => SerializeCanonicalIdentity(
                nameof(StartRun),
                start.Precondition.SessionId.Value,
                start.Precondition.SessionVersion.ToString(
                    CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(
                    start.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions)),
            PauseRun pause => SerializeCanonicalIdentity(
                nameof(PauseRun),
                pause.Precondition.SessionId.Value,
                pause.Precondition.RunGeneration.Value.ToString(
                    CultureInfo.InvariantCulture)),
            HotSwapSession hotSwap => SerializeCanonicalIdentity(
                nameof(HotSwapSession),
                hotSwap.Precondition.SessionId.Value,
                hotSwap.Precondition.SessionVersion.ToString(
                    CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(
                    hotSwap.Precondition.CompilationArtifactKey,
                    CanonicalJsonOptions),
                JsonSerializer.Serialize(
                    hotSwap.TargetCompilationArtifactKey,
                    CanonicalJsonOptions)),
            ClaimSandbox claim => SerializeCanonicalIdentity(
                nameof(ClaimSandbox),
                claim.Precondition.ProjectRevisionId.Value,
                claim.RequestedDisplayName),
            SaveDurable save => SerializeCanonicalIdentity(
                nameof(SaveDurable),
                save.Precondition.ProjectRevisionId.Value,
                save.Precondition.ExpectedDurableVersion.Value),
            PrepareExport prepare => SerializeCanonicalIdentity(
                nameof(PrepareExport),
                prepare.Precondition.ProjectRevisionId.Value,
                prepare.ProjectRevisionId.Value),
            CloseWorkspace => SerializeCanonicalIdentity(nameof(CloseWorkspace)),
            _ => SerializeCanonicalIdentity(
                command.GetType().FullName ?? command.GetType().Name),
        };
    }

    private static string CanonicalReplaceProbesIdentity(ReplaceProbes command)
    {
        var components = new List<string>(command.Bindings.Count + 3)
        {
            command.Precondition.SessionId.Value,
            command.Precondition.SessionVersion.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(
                command.Precondition.CompilationArtifactKey,
                CanonicalJsonOptions),
        };
        components.AddRange(command.Bindings.Select(binding => binding switch
        {
            RetainProbe retain => SerializeCanonicalIdentity(
                nameof(RetainProbe),
                retain.ProbeId.Value,
                JsonSerializer.Serialize(retain.Source, CanonicalJsonOptions)),
            CreateProbe create => SerializeCanonicalIdentity(
                nameof(CreateProbe),
                JsonSerializer.Serialize(create.Source, CanonicalJsonOptions)),
            _ => throw new InvalidOperationException(
                "The Workspace Probe binding request variant is undefined."),
        }));
        return SerializeCanonicalIdentity(nameof(ReplaceProbes), [.. components]);
    }

    private static string SerializeCanonicalIdentity(
        string commandKind,
        params string[] components)
    {
        string[] fields = [commandKind, .. components];
        var identity = new StringBuilder();
        identity.Append(fields.Length.ToString(CultureInfo.InvariantCulture));
        identity.Append(';');
        foreach (var field in fields)
        {
            identity.Append(field.Length.ToString(CultureInfo.InvariantCulture));
            identity.Append(':');
            identity.Append(field);
        }

        return identity.ToString();
    }

    private static JsonSerializerOptions CanonicalJsonOptions { get; } = new()
    {
        TypeInfoResolver = new DomainPolymorphicTypeResolver(),
        Converters = { new Utf16CodeUnitStringJsonConverter() },
    };

    private sealed class Utf16CodeUnitStringJsonConverter : JsonConverter<string>
    {
        private const string HexDigits = "0123456789abcdef";

        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            string value,
            JsonSerializerOptions options) => writer.WriteStringValue(Encode(value));

        public override string ReadAsPropertyName(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) => throw new NotSupportedException();

        public override void WriteAsPropertyName(
            Utf8JsonWriter writer,
            string value,
            JsonSerializerOptions options) => writer.WritePropertyName(Encode(value));

        private static string Encode(string value)
        {
            return string.Create(
                checked(value.Length * 4),
                value,
                static (destination, source) =>
                {
                    var offset = 0;
                    foreach (var codeUnit in source)
                    {
                        destination[offset++] = HexDigits[codeUnit >> 12];
                        destination[offset++] = HexDigits[(codeUnit >> 8) & 0x0f];
                        destination[offset++] = HexDigits[(codeUnit >> 4) & 0x0f];
                        destination[offset++] = HexDigits[codeUnit & 0x0f];
                    }
                });
        }
    }

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

    private sealed record RunPauseRequest(
        PendingIntent PendingIntent,
        RunGeneration RunGeneration);

    private abstract record ContextualIntentInspection;

    private sealed record ContextualIntentTerminal(WorkspaceCommandOutcome Outcome)
        : ContextualIntentInspection;

    private sealed record ContextualIntentReplay(
        PendingIntent PendingIntent,
        PendingReplayWaiter Waiter)
        : ContextualIntentInspection;

    private sealed record ContextualIntentAccepted(string CanonicalIdentity)
        : ContextualIntentInspection;
}
