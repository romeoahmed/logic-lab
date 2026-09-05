using System.Diagnostics;
using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Work;

internal sealed partial class WorkCoordinator
{
    private abstract class WorkItem : IDisposable
    {
        private readonly Lock cancellationGate = new();
        private CancellationTokenSource? cancellation;

        protected WorkItem(
            CancellationToken callerCancellationToken,
            CancellationToken stoppingToken)
            : this(
                CancellationTokenSource.CreateLinkedTokenSource(
                    callerCancellationToken,
                    stoppingToken))
        {
        }

        private WorkItem(CancellationTokenSource ownedCancellation)
        {
            cancellation = ownedCancellation;
            CancellationToken = cancellation.Token;
            ParentActivityContext = Activity.Current?.Context;
            Correlation = ApplicationCorrelation.CurrentOrCreate();
        }

        public CancellationToken CancellationToken { get; }

        public ActivityContext? ParentActivityContext { get; }

        public string Correlation { get; }

        protected void Cancel()
        {
            lock (cancellationGate)
            {
                cancellation?.Cancel();
            }
        }

        public void Dispose()
        {
            lock (cancellationGate)
            {
                cancellation?.Dispose();
                cancellation = null;
            }
        }
    }

    private sealed class CompilationWorkItem(
        WorkspaceId workspaceId,
        Func<CompilationWorkContext, ValueTask> operation,
        Action releaseOwnership,
        CancellationToken operationCancellationToken,
        CancellationToken stoppingToken)
        : WorkItem(operationCancellationToken, stoppingToken)
    {
        private Action? ownershipRelease = releaseOwnership;

        public WorkspaceId WorkspaceId { get; } = workspaceId;

        public LinkedListNode<WorkspaceId>? QueueNode { get; private set; }

        public Func<CompilationWorkContext, ValueTask> Operation { get; }
            = operation;

        public bool WasPublishedUnderLock { get; private set; }

        public void MarkQueuedUnderLock(LinkedListNode<WorkspaceId> queueNode)
        {
            QueueNode = queueNode;
        }

        public void MarkRemovedUnderLock()
        {
            QueueNode = null;
        }

        public void CancelSuperseded()
        {
            Cancel();
        }

        public void Abandon()
        {
            try
            {
                Cancel();
            }
            finally
            {
                ReleaseOwnership();
            }
        }

        public void ReleaseOwnership()
        {
            try
            {
                Interlocked.Exchange(ref ownershipRelease, null)?.Invoke();
            }
            finally
            {
                Dispose();
            }
        }

        public void MarkPublishedUnderLock() => WasPublishedUnderLock = true;
    }

    private sealed class SessionWorkItem(
        WorkspaceId workspaceId,
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
        SessionContinuation? continuation,
        TaskCompletionSource<WorkspaceCommandOutcome>? completion,
        CancellationToken callerCancellationToken,
        CancellationToken stoppingToken)
        : WorkItem(callerCancellationToken, stoppingToken)
    {
        public WorkspaceId WorkspaceId { get; } = workspaceId;

        public LinkedListNode<SessionWorkItem>? QueueNode { get; private set; }

        public Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> Operation { get; }
            = operation;

        public SessionContinuation? Continuation { get; } = continuation;

        public TaskCompletionSource<WorkspaceCommandOutcome>? Completion { get; } = completion;

        public void Complete(WorkspaceCommandOutcome outcome)
        {
            Completion?.TrySetResult(outcome);
        }

        public void MarkQueuedUnderLock(LinkedListNode<SessionWorkItem> queueNode)
        {
            QueueNode = queueNode;
        }

        public void MarkRemovedUnderLock()
        {
            QueueNode = null;
        }

        public bool IsQueuedUnderLock() => QueueNode is not null;

        public void CancelScheduledWork()
        {
            Cancel();
        }

        public static SessionWorkItem CreateCommand(
            WorkspaceId workspaceId,
            Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
            CancellationToken callerCancellationToken,
            CancellationToken stoppingToken)
        {
            return new SessionWorkItem(
                workspaceId,
                operation,
                continuation: null,
                new TaskCompletionSource<WorkspaceCommandOutcome>(
                    TaskCreationOptions.RunContinuationsAsynchronously),
                callerCancellationToken,
                stoppingToken);
        }

        public static SessionWorkItem CreateContinuation(
            Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
            SessionContinuation continuation,
            CancellationToken stoppingToken)
        {
            return new SessionWorkItem(
                continuation.WorkspaceId,
                operation,
                continuation,
                completion: null,
                CancellationToken.None,
                stoppingToken);
        }
    }

    internal sealed class ScheduledSessionWork
    {
        private readonly Action cancel;

        internal ScheduledSessionWork(
            Task<WorkspaceCommandOutcome> completion,
            Action cancel)
        {
            Completion = completion;
            this.cancel = cancel;
        }

        internal Task<WorkspaceCommandOutcome> Completion { get; }

        internal void Cancel() => cancel();
    }

    internal sealed class ScheduledCompilationWork(Action cancel)
    {
        internal void Cancel() => cancel();
    }

    internal sealed class SessionContinuation(
        WorkCoordinator owner,
        WorkspaceId workspaceId)
    {
        private bool isExecuting;
        private bool isQueued;
        private bool isReserved = true;

        internal WorkspaceId WorkspaceId { get; } = workspaceId;

        internal bool TrySchedule(
            Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            return owner.TryScheduleSessionContinuation(this, operation);
        }

        internal bool CanScheduleUnderLock()
        {
            return isReserved
                && isExecuting
                && !isQueued;
        }

        internal void MarkQueuedUnderLock()
        {
            isQueued = true;
        }

        internal void MarkExecutingUnderLock()
        {
            isQueued = false;
            isExecuting = true;
        }

        internal bool CompleteExecutionUnderLock()
        {
            isExecuting = false;
            if (isQueued)
            {
                return false;
            }

            isReserved = false;
            return true;
        }

        internal bool AbandonUnderLock()
        {
            isExecuting = false;
            isQueued = false;
            if (!isReserved)
            {
                return false;
            }

            isReserved = false;
            return true;
        }
    }
}
