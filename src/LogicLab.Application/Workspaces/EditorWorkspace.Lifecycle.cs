using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Work;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    public ValueTask DisposeAsync()
    {
        Task completion;
        Task? drainToRun = null;
        TaskCompletionSource? completionToRun = null;
        lock (operationAdmissionGate)
        {
            if (disposalTask is null)
            {
                operationAdmissionClosed = true;
                drainToRun = activeOperations == 0
                    ? Task.CompletedTask
                    : (operationsDrained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                completionToRun = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                disposalTask = completionToRun.Task;
            }

            completion = disposalTask;
        }

        if (completionToRun is not null)
        {
            _ = CompleteDisposalAsync(drainToRun!, completionToRun);
        }

        return new ValueTask(completion);
    }

    private async Task CompleteDisposalAsync(
        Task operationDrain,
        TaskCompletionSource completion)
    {
        try
        {
            lock (gate)
            {
                isDisposed = true;
            }

            var laneDrain = workCoordinator.DisposeAsync().AsTask();
            await Task.WhenAll(operationDrain, laneDrain).ConfigureAwait(false);

            WorkspaceState[] retired;
            lock (gate)
            {
                retired = [.. workspaces.Values];
                workspaces.Clear();
                foreach (var state in retired)
                {
                    state.IsRetired = true;
                }
            }

            RetireAll(retired);
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private WorkspaceOperationLease? TryEnterOperation()
    {
        lock (operationAdmissionGate)
        {
            if (operationAdmissionClosed)
            {
                return null;
            }

            activeOperations++;
            return new WorkspaceOperationLease(ExitOperation);
        }
    }

    private void ExitOperation()
    {
        TaskCompletionSource? drained = null;
        lock (operationAdmissionGate)
        {
            activeOperations--;
            if (operationAdmissionClosed && activeOperations == 0)
            {
                drained = operationsDrained;
            }
        }

        drained?.TrySetResult();
    }

    private WorkspaceCommandOutcome Close(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        lock (gate)
        {
            if (!IsCurrentWorkspaceUnderLock(state))
            {
                return Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            RemoveWorkspaceUnderLock(state);
        }

        return new WorkspaceClosed(state.Id);
    }

    private WorkspaceAcquisition AcquireWorkspace(WorkspaceId workspaceId)
    {
        WorkspaceState expired;
        lock (gate)
        {
            if (isDisposed)
            {
                return WorkspaceAcquisition.Rejected(
                    WorkspaceOutcomeReasons.WorkspaceCancelled);
            }

            if (!workspaces.TryGetValue(workspaceId, out var state))
            {
                return WorkspaceAcquisition.Rejected(
                    WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var nowTimestamp = timeProvider.GetTimestamp();
            if (!IsExpired(state, nowTimestamp))
            {
                state.LeaseCount++;
                return WorkspaceAcquisition.Acquired(this, state);
            }

            RemoveWorkspaceUnderLock(state);
            expired = state;
        }

        Retire(expired);
        return WorkspaceAcquisition.Rejected(WorkspaceOutcomeReasons.WorkspaceNotFound);
    }

    private string? ReserveWorkspace(
        WorkspaceCaller caller,
        out List<WorkspaceState> retired,
        out PolicyEvidenceProjection? policyEvidence)
    {
        ArgumentNullException.ThrowIfNull(caller);
        retired = [];
        policyEvidence = null;
        lock (gate)
        {
            if (isDisposed)
            {
                return WorkspaceOutcomeReasons.WorkspaceCancelled;
            }

            retired = ReclaimExpiredUnderLock(timeProvider.GetTimestamp());
            if ((long)workspaces.Count + workspaceReservations
                >= workspacePolicy.GlobalWorkspaceLimit)
            {
                policyEvidence = WorkspacePolicyEvidence(
                    "global_workspace_count",
                    (ulong)workspaces.Count + (ulong)workspaceReservations + 1);
                return WorkspaceOutcomeReasons.WorkspaceAdmissionRejected;
            }

            if (IsAnonymous(caller)
                && anonymousWorkspaceCount >= workspacePolicy.AnonymousWorkspaceLimit)
            {
                policyEvidence = WorkspacePolicyEvidence(
                    "anonymous_workspace_count_global",
                    (ulong)anonymousWorkspaceCount + 1);
                return WorkspaceOutcomeReasons.WorkspaceAdmissionRejected;
            }

            _ = workspaceCountsByCaller.TryGetValue(caller, out var subjectCount);
            if (subjectCount >= workspacePolicy.WorkspaceCountPerSubject)
            {
                policyEvidence = WorkspacePolicyEvidence(
                    "workspace_count_per_subject",
                    (ulong)subjectCount + 1);
                return WorkspaceOutcomeReasons.WorkspaceAdmissionRejected;
            }

            workspaceReservations++;
            IncrementWorkspaceCountUnderLock(caller, subjectCount);
            return null;
        }
    }

    private void ReleaseWorkspaceReservation(WorkspaceCaller caller)
    {
        lock (gate)
        {
            workspaceReservations--;
            DecrementWorkspaceCountUnderLock(caller);
        }
    }

    private string? PublishWorkspaceReservationUnderLock(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        workspaceReservations--;
        if (isDisposed || cancellationToken.IsCancellationRequested)
        {
            DecrementWorkspaceCountUnderLock(state.AdmissionCaller);
            return WorkspaceOutcomeReasons.WorkspaceCancelled;
        }

        state.LastAccessTimestamp = timeProvider.GetTimestamp();
        try
        {
            workspaces.Add(state.Id, state);
        }
        catch
        {
            DecrementWorkspaceCountUnderLock(state.AdmissionCaller);
            throw;
        }

        return null;
    }

    private List<WorkspaceState> ReclaimExpiredUnderLock(long nowTimestamp)
    {
        var expired = workspaces.Values
            .Where(state => IsExpired(state, nowTimestamp))
            .ToList();
        foreach (var state in expired)
        {
            RemoveWorkspaceUnderLock(state);
        }

        return expired;
    }

    private void RemoveWorkspaceUnderLock(WorkspaceState state)
    {
        _ = workspaces.Remove(state.Id);
        state.IsRetired = true;
        DecrementWorkspaceCountUnderLock(state.AdmissionCaller);
        if (state.PendingAdmissionCaller is { } pendingCaller)
        {
            DecrementWorkspaceCountUnderLock(pendingCaller);
            state.PendingAdmissionCaller = null;
        }
    }

    private bool TryReserveWorkspaceAdmissionTransfer(
        WorkspaceState state,
        WorkspaceCaller targetCaller,
        [NotNullWhen(false)] out PolicyEvidenceProjection? policyEvidence)
    {
        policyEvidence = null;
        lock (gate)
        {
            if (state.AdmissionCaller == targetCaller
                || state.PendingAdmissionCaller == targetCaller)
            {
                return true;
            }

            _ = workspaceCountsByCaller.TryGetValue(targetCaller, out var subjectCount);
            if (IsAnonymous(targetCaller)
                && anonymousWorkspaceCount >= workspacePolicy.AnonymousWorkspaceLimit)
            {
                policyEvidence = WorkspacePolicyEvidence(
                    "anonymous_workspace_count_global",
                    (ulong)anonymousWorkspaceCount + 1);
                return false;
            }

            if (subjectCount >= workspacePolicy.WorkspaceCountPerSubject)
            {
                policyEvidence = WorkspacePolicyEvidence(
                    "workspace_count_per_subject",
                    (ulong)subjectCount + 1);
                return false;
            }

            IncrementWorkspaceCountUnderLock(targetCaller, subjectCount);
            state.PendingAdmissionCaller = targetCaller;
            return true;
        }
    }

    private void CommitWorkspaceAdmissionTransfer(WorkspaceState state)
    {
        lock (gate)
        {
            if (state.PendingAdmissionCaller is not { } targetCaller)
            {
                return;
            }

            DecrementWorkspaceCountUnderLock(state.AdmissionCaller);
            state.AdmissionCaller = targetCaller;
            state.PendingAdmissionCaller = null;
        }
    }

    private void ReleaseWorkspaceAdmissionTransfer(WorkspaceState state)
    {
        lock (gate)
        {
            if (state.PendingAdmissionCaller is not { } targetCaller)
            {
                return;
            }

            DecrementWorkspaceCountUnderLock(targetCaller);
            state.PendingAdmissionCaller = null;
        }
    }

    private void DecrementWorkspaceCountUnderLock(WorkspaceCaller caller)
    {
        if (IsAnonymous(caller))
        {
            anonymousWorkspaceCount--;
        }

        var remaining = workspaceCountsByCaller[caller] - 1;
        if (remaining == 0)
        {
            _ = workspaceCountsByCaller.Remove(caller);
        }
        else
        {
            workspaceCountsByCaller[caller] = remaining;
        }
    }

    private void IncrementWorkspaceCountUnderLock(
        WorkspaceCaller caller,
        int currentCallerCount)
    {
        workspaceCountsByCaller[caller] = currentCallerCount + 1;
        if (IsAnonymous(caller))
        {
            anonymousWorkspaceCount++;
        }
    }

    private static bool IsAnonymous(WorkspaceCaller caller) =>
        caller is not AuthenticatedWorkspaceCaller;

    private PolicyEvidenceProjection WorkspacePolicyEvidence(
        string dimension,
        ulong observed)
    {
        return new PolicyEvidenceProjection(
            workspacePolicy.PolicyId,
            workspacePolicy.PolicyRevision,
            dimension,
            observed);
    }

    private bool IsExpired(WorkspaceState state, long nowTimestamp)
    {
        if (state.DetachedAtTimestamp is { } detachedAtTimestamp)
        {
            return timeProvider.GetElapsedTime(detachedAtTimestamp, nowTimestamp)
                >= workspacePolicy.DetachedRetention;
        }

        return state.LeaseCount == 0
            && timeProvider.GetElapsedTime(state.LastAccessTimestamp, nowTimestamp)
            >= workspacePolicy.SandboxRetention;
    }

    private void TouchWorkspace(WorkspaceState state)
    {
        lock (gate)
        {
            if (IsCurrentWorkspaceUnderLock(state))
            {
                state.LastAccessTimestamp = timeProvider.GetTimestamp();
            }
        }
    }

    private bool IsCurrentWorkspace(WorkspaceState state)
    {
        lock (gate)
        {
            return IsCurrentWorkspaceUnderLock(state);
        }
    }

    private bool IsCurrentWorkspaceUnderLock(WorkspaceState state)
    {
        return !state.IsRetired
            && workspaces.TryGetValue(state.Id, out var current)
            && ReferenceEquals(current, state);
    }

    private void Release(WorkspaceState state)
    {
        bool shouldRetire;
        lock (gate)
        {
            state.LeaseCount--;
            shouldRetire = state.IsRetired
                && state.LeaseCount == 0
                && !state.ResourcesDisposed;
        }

        if (shouldRetire)
        {
            Retire(state);
        }
    }

    private bool TryRetainWorkspace(
        WorkspaceState state,
        [NotNullWhen(false)] out string? rejectionCode)
    {
        lock (gate)
        {
            if (!IsCurrentWorkspaceUnderLock(state))
            {
                rejectionCode = isDisposed
                    ? WorkspaceOutcomeReasons.WorkspaceCancelled
                    : WorkspaceOutcomeReasons.WorkspaceNotFound;
                return false;
            }

            state.LeaseCount++;
            rejectionCode = null;
            return true;
        }
    }

    private void RetireAll(IEnumerable<WorkspaceState> states)
    {
        foreach (var state in states)
        {
            Retire(state);
        }
    }

    private void Retire(WorkspaceState state)
    {
        lock (gate)
        {
            if (!state.IsRetired || state.LeaseCount != 0 || state.ResourcesDisposed)
            {
                return;
            }

            state.ResourcesDisposed = true;
        }

        if (state.SessionHandle is not null)
        {
            CloseSimulationForCleanup(state.SessionHandle);
            state.SessionHandle = null;
        }

        state.CommandGate.Dispose();
        state.AuthorizationAdmission.Dispose();
    }

    private void CloseSimulationForCleanup(SimulationSessionHandle handle)
    {
        try
        {
            _ = operations.CloseSimulation(handle);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var correlation = ApplicationCorrelation.CurrentOrCreate();
            LogSimulationCleanupFailure(logger, exception, correlation);
        }
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Simulation cleanup failed with correlation {Correlation}.")]
    private static partial void LogSimulationCleanupFailure(
        ILogger logger,
        Exception exception,
        string correlation);

    private sealed class WorkspaceState(
        WorkspaceId id,
        ProjectRevision revision,
        WorkspaceCaller admissionCaller,
        long lastAccessTimestamp)
    {
        public WorkspaceId Id { get; } = id;

        public ulong ProjectionVersion { get; set; } = 1;

        public ProjectRevision Revision { get; set; } = revision;

        public WorkspaceCaller AdmissionCaller { get; set; } = admissionCaller;

        public WorkspaceCaller? PendingAdmissionCaller { get; set; }

        public List<ProjectRevision> History { get; } = [revision];

        public int HistoryCursor { get; set; }

        public WorkspaceAttachmentId? AttachmentId { get; set; }

        public ulong AttachmentGeneration { get; set; }

        public bool IsAttached { get; set; }

        public long? DetachedAtTimestamp { get; set; }

        public Dictionary<ClientIntentId, IdempotencyRecord> IdempotencyRecords { get; } = [];

        public Dictionary<ClientIntentId, PendingIntent> PendingIntents { get; } = [];

        public Queue<ClientIntentId> IdempotencyOrder { get; } = [];

        public bool IsIdempotencyWindowClosed { get; set; }

        public WorkspaceDurabilityState Durability { get; set; } =
            SandboxWorkspaceState.Instance;

        public CompilationArtifact? Artifact { get; set; }

        public CompilationProjection Compilation { get; set; } =
            CompilationNotRequestedProjection.Instance;

        public ulong NextCompilationGeneration { get; set; }

        public SimulationSessionHandle? SessionHandle { get; set; }

        public SimulationProjection? Simulation { get; set; }

        public ulong NextRunGeneration { get; set; }

        public RunPauseRequest? PendingRunPause { get; set; }

        public long LastAccessTimestamp { get; set; } = lastAccessTimestamp;

        public int LeaseCount { get; set; }

        public bool IsRetired { get; set; }

        public bool ResourcesDisposed { get; set; }

        public Lock ContinuityGate { get; } = new();

        public SemaphoreSlim CommandGate { get; } = new(1, 1);

        public AuthorizationAdmissionEpoch AuthorizationAdmission { get; set; } = new();
    }

    private sealed record IdempotencyRecord(
        string CanonicalIdentity,
        WorkspaceCommandOutcome Outcome);

    private sealed class PendingIntent(
        WorkspaceCommandContext context,
        string canonicalIdentity,
        TaskCompletionSource<WorkspaceCommandOutcome> completion)
    {
        public WorkspaceCommandContext Context { get; } = context;

        public string CanonicalIdentity { get; } = canonicalIdentity;

        public TaskCompletionSource<WorkspaceCommandOutcome> Completion { get; } =
            completion;

        public List<PendingReplayWaiter> ReplayWaiters { get; } = [];

        public WorkCoordinator.ScheduledSessionWork? ScheduledSessionWork { get; set; }
    }

    private sealed class PendingReplayWaiter(WorkspaceCommandContext context)
    {
        public WorkspaceCommandContext Context { get; } = context;

        public TaskCompletionSource<WorkspaceCommandOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AuthorizationAdmissionEpoch : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private int referenceCount = 1;

        public AuthorizationAdmissionLease Acquire()
        {
            _ = Interlocked.Increment(ref referenceCount);
            return new AuthorizationAdmissionLease(Release, cancellation.Token);
        }

        public void Revoke()
        {
            try
            {
                cancellation.Cancel();
            }
            finally
            {
                Release();
            }
        }

        public void Dispose() => Release();

        private void Release()
        {
            if (Interlocked.Decrement(ref referenceCount) == 0)
            {
                cancellation.Dispose();
            }
        }

        public sealed class AuthorizationAdmissionLease(
            Action release,
            CancellationToken cancellationToken) : IDisposable
        {
            private Action? releaseReference = release;

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public void Dispose()
            {
                Interlocked.Exchange(ref releaseReference, null)?.Invoke();
            }
        }
    }

    private abstract class WorkspaceDurabilityState
    {
        public abstract AuthenticatedSubjectId? OwnerSubjectId { get; }
    }

    private sealed class SandboxWorkspaceState : WorkspaceDurabilityState
    {
        public static SandboxWorkspaceState Instance { get; } = new();

        public override AuthenticatedSubjectId? OwnerSubjectId => null;
    }

    private sealed class PendingDurableClaimState(
        AuthenticatedSubjectId subjectId) : WorkspaceDurabilityState
    {
        public override AuthenticatedSubjectId OwnerSubjectId { get; } = subjectId;
    }

    private sealed class DurableWorkspaceState(
        DurableProjectId durableProjectId,
        AuthenticatedSubjectId subjectId,
        DurableDisplayName displayName,
        DurableVersion observedDurableVersion,
        ProjectRevisionId savedProjectRevisionId) : WorkspaceDurabilityState
    {
        public DurableProjectId DurableProjectId { get; } = durableProjectId;

        public override AuthenticatedSubjectId OwnerSubjectId { get; } = subjectId;

        public DurableDisplayName DisplayName { get; } = displayName;

        public DurableVersion ObservedDurableVersion { get; set; } =
            observedDurableVersion;

        public ProjectRevisionId SavedProjectRevisionId { get; set; } =
            savedProjectRevisionId;

        public DurableVersion? ConflictActualDurableVersion { get; set; }

        public DurableWorkspaceState Copy()
        {
            return new DurableWorkspaceState(
                DurableProjectId,
                OwnerSubjectId,
                DisplayName,
                ObservedDurableVersion,
                SavedProjectRevisionId)
            {
                ConflictActualDurableVersion = ConflictActualDurableVersion,
            };
        }
    }

    private sealed class WorkspaceAcquisition : IDisposable
    {
        private EditorWorkspace? owner;

        private WorkspaceAcquisition(
            EditorWorkspace? owner,
            WorkspaceState? state,
            string? rejectionReason)
        {
            this.owner = owner;
            State = state;
            RejectionReason = rejectionReason;
        }

        public WorkspaceState? State { get; }

        public string? RejectionReason { get; }

        [MemberNotNullWhen(true, nameof(State))]
        [MemberNotNullWhen(false, nameof(RejectionReason))]
        public bool IsAcquired => State is not null;

        public void Dispose()
        {
            if (IsAcquired)
            {
                Interlocked.Exchange(ref owner, null)?.Release(State);
            }
        }

        public static WorkspaceAcquisition Acquired(
            EditorWorkspace owner,
            WorkspaceState state)
            => new(owner, state, null);

        public static WorkspaceAcquisition Rejected(string reason)
            => new(null, null, reason);
    }

    private sealed class WorkspaceOperationLease(Action release) : IDisposable
    {
        private Action? releaseReference = release;

        public void Dispose()
        {
            Interlocked.Exchange(ref releaseReference, null)?.Invoke();
        }
    }
}
