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
            retired = workspaces.Values.ToArray();
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
            if (state.IsRetired
                || !workspaces.TryGetValue(state.Id, out var current)
                || !ReferenceEquals(current, state))
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
        WorkspaceState? expired = null;
        WorkspaceLease? lease = null;
        string? rejectionReason = null;
        lock (gate)
        {
            if (isDisposed)
            {
                rejectionReason = WorkspaceOutcomeReasons.WorkspaceCancelled;
            }
            else if (!workspaces.TryGetValue(workspaceId, out var state))
            {
                rejectionReason = WorkspaceOutcomeReasons.WorkspaceNotFound;
            }
            else
            {
                var now = timeProvider.GetUtcNow();
                if (state.LeaseCount == 0 && IsExpired(state, now))
                {
                    workspaces.Remove(workspaceId);
                    state.IsRetired = true;
                    expired = state;
                    rejectionReason = WorkspaceOutcomeReasons.WorkspaceExpired;
                }
                else
                {
                    state.LeaseCount++;
                    state.LastAccessUtc = now;
                    lease = new WorkspaceLease(this, state);
                }
            }
        }

        if (expired is not null)
        {
            Retire(expired);
        }

        return new WorkspaceAcquisition(lease, rejectionReason);
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

            retired = ReclaimExpiredUnderLock(timeProvider.GetUtcNow());
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

    private List<WorkspaceState> ReclaimExpiredUnderLock(DateTimeOffset now)
    {
        var expired = workspaces.Values
            .Where(state => state.LeaseCount == 0 && IsExpired(state, now))
            .ToList();
        foreach (var state in expired)
        {
            workspaces.Remove(state.Id);
            state.IsRetired = true;
        }

        return expired;
    }

    private bool IsExpired(WorkspaceState state, DateTimeOffset now)
    {
        return now - state.LastAccessUtc >= workspacePolicy.SandboxRetention;
    }

    private void Release(WorkspaceState state)
    {
        var shouldRetire = false;
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
            try
            {
                _ = operations.CloseSimulation(state.SessionHandle);
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                var correlation = Guid.CreateVersion7().ToString("N");
                LogRetirementFailure(logger, exception, correlation);
            }

            state.SessionHandle = null;
        }

        state.CommandGate.Dispose();
    }

    private static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Workspace retirement failed with correlation {Correlation}.")]
    private static partial void LogRetirementFailure(
        ILogger logger,
        Exception exception,
        string correlation);

    private sealed class WorkspaceState(
        WorkspaceId id,
        ProjectRevision revision,
        DateTimeOffset lastAccessUtc)
    {
        public WorkspaceId Id { get; } = id;

        public ulong ProjectionVersion { get; set; } = 1;

        public ProjectRevision Revision { get; set; } = revision;

        public CompilationArtifact? Artifact { get; set; }

        public CompilationProjection Compilation { get; set; } = NotRequestedCompilation();

        public SimulationSessionHandle? SessionHandle { get; set; }

        public SimulationProjection? Simulation { get; set; }

        public DateTimeOffset LastAccessUtc { get; set; } = lastAccessUtc;

        public int LeaseCount { get; set; }

        public bool IsRetired { get; set; }

        public bool ResourcesDisposed { get; set; }

        public SemaphoreSlim CommandGate { get; } = new(1, 1);
    }

    private sealed class WorkspaceLease(EditorWorkspace owner, WorkspaceState state) : IDisposable
    {
        private EditorWorkspace? owner = owner;

        public WorkspaceState State { get; } = state;

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Release(State);
        }
    }

    private readonly record struct WorkspaceAcquisition(
        WorkspaceLease? Lease,
        string? RejectionReason);
}
