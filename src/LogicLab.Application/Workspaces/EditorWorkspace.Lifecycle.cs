using LogicLab.Application.Work;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    public async ValueTask DisposeAsync()
    {
        WorkspaceState[] retired;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            retired = [.. workspaces.Values];
            workspaces.Clear();
            foreach (var state in retired)
            {
                state.IsRetired = true;
            }
        }

        await workCoordinator.DisposeAsync().ConfigureAwait(false);
        RetireAll(retired);
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

            workspaces.Remove(state.Id);
            state.IsRetired = true;
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

            workspaces.Remove(workspaceId);
            state.IsRetired = true;
            expired = state;
        }

        Retire(expired);
        return WorkspaceAcquisition.Rejected(WorkspaceOutcomeReasons.WorkspaceNotFound);
    }

    private string? ReserveWorkspace(out List<WorkspaceState> retired)
    {
        retired = [];
        lock (gate)
        {
            if (isDisposed)
            {
                return WorkspaceOutcomeReasons.WorkspaceCancelled;
            }

            retired = ReclaimExpiredUnderLock(timeProvider.GetTimestamp());
            if (workspaces.Count + workspaceReservations
                >= workspacePolicy.GlobalWorkspaceLimit)
            {
                return WorkspaceOutcomeReasons.WorkspaceAdmissionRejected;
            }

            workspaceReservations++;
            return null;
        }
    }

    private void ReleaseWorkspaceReservation()
    {
        lock (gate)
        {
            workspaceReservations--;
        }
    }

    private List<WorkspaceState> ReclaimExpiredUnderLock(long nowTimestamp)
    {
        var expired = workspaces.Values
            .Where(state => IsExpired(state, nowTimestamp))
            .ToList();
        foreach (var state in expired)
        {
            workspaces.Remove(state.Id);
            state.IsRetired = true;
        }

        return expired;
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
        out string? rejectionCode)
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

        if (state.ActiveSession is not null)
        {
            CloseSimulationForCleanup(state.ActiveSession.Handle);
            state.ActiveSession = null;
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
            var correlation = Guid.CreateVersion7().ToString("N");
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
        long lastAccessTimestamp)
    {
        public WorkspaceId Id { get; } = id;

        public ulong ProjectionVersion { get; set; } = 1;

        public ProjectRevision Revision { get; set; } = revision;

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

        public ActiveSessionContext? ActiveSession { get; set; }

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
        WorkspaceCaller caller,
        string canonicalIdentity,
        TaskCompletionSource<WorkspaceCommandOutcome> completion)
    {
        public WorkspaceCaller Caller { get; } = caller;

        public string CanonicalIdentity { get; } = canonicalIdentity;

        public TaskCompletionSource<WorkspaceCommandOutcome> Completion { get; } =
            completion;

        public WorkCoordinator.ScheduledSessionWork? ScheduledSessionWork { get; set; }
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

    private abstract class WorkspaceDurabilityState(
        AuthenticatedSubjectId? ownerSubjectId)
    {
        public AuthenticatedSubjectId? OwnerSubjectId { get; } = ownerSubjectId;
    }

    private sealed class SandboxWorkspaceState() : WorkspaceDurabilityState(null)
    {
        public static SandboxWorkspaceState Instance { get; } = new();
    }

    private sealed class PendingDurableClaimState(
        AuthenticatedSubjectId subjectId) : WorkspaceDurabilityState(subjectId);

    private sealed class DurableWorkspaceState(
        DurableProjectId durableProjectId,
        AuthenticatedSubjectId subjectId,
        DurableDisplayName displayName,
        DurableVersion observedDurableVersion,
        ProjectRevisionId savedProjectRevisionId) : WorkspaceDurabilityState(subjectId)
    {
        public DurableProjectId DurableProjectId { get; } = durableProjectId;

        public AuthenticatedSubjectId SubjectId { get; } = subjectId;

        public DurableDisplayName DisplayName { get; } = displayName;

        public DurableVersion ObservedDurableVersion { get; set; } =
            observedDurableVersion;

        public ProjectRevisionId SavedProjectRevisionId { get; set; } =
            savedProjectRevisionId;

        public DurableVersion? ConflictActualDurableVersion { get; set; }

        public DurableWorkspaceState Copy()
        {
            var copy = new DurableWorkspaceState(
                DurableProjectId,
                SubjectId,
                DisplayName,
                ObservedDurableVersion,
                SavedProjectRevisionId);
            copy.ConflictActualDurableVersion = ConflictActualDurableVersion;
            return copy;
        }
    }

    private sealed record ActiveSessionContext(
        SimulationSessionHandle Handle,
        ProjectRevision ProjectRevision,
        CompilationArtifact Artifact);

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

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Release(State!);
        }

        public static WorkspaceAcquisition Acquired(
            EditorWorkspace owner,
            WorkspaceState state)
            => new(owner, state, null);

        public static WorkspaceAcquisition Rejected(string reason)
            => new(null, null, reason);
    }
}
