using System.Threading.Channels;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Work;

internal sealed partial class WorkCoordinator : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Channel<WorkspaceId> compilationQueue;
    private readonly Channel<SessionWorkItem> sessionQueue;
    private readonly int sessionQueueCapacity;
    private readonly ILogger<WorkCoordinator> logger;
    private readonly Dictionary<WorkspaceId, CompilationWorkItem> latestCompilations = [];
    private readonly Dictionary<WorkspaceId, CompilationWorkItem> pendingCompilations = [];
    private readonly Task compilationWorker;
    private readonly Task sessionWorker;
    private int reservedSessionItems;
    private bool isDisposed;

    public WorkCoordinator(
        SchedulingPolicy policy,
        ILogger<WorkCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        compilationQueue = CreateBoundedQueue<WorkspaceId>(
            policy.CompilationQueueCapacity);
        sessionQueue = CreateBoundedQueue<SessionWorkItem>(policy.SessionQueueCapacity);
        sessionQueueCapacity = policy.SessionQueueCapacity;
        compilationWorker = ConsumeCompilationsAsync();
        sessionWorker = ConsumeSessionsAsync();
    }

    internal bool TryScheduleCompilation(
        WorkspaceId workspaceId,
        Func<CompilationWorkContext, ValueTask> operation,
        Action releaseOwnership,
        CancellationToken admissionCancellationToken,
        out string? rejectionCode)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(releaseOwnership);
        CompilationWorkItem item;
        CompilationWorkItem? superseded = null;
        var disposeSuperseded = false;
        lock (gate)
        {
            if (isDisposed || admissionCancellationToken.IsCancellationRequested)
            {
                rejectionCode = WorkspaceOutcomeReasons.WorkspaceCancelled;
                return false;
            }

            item = new CompilationWorkItem(
                workspaceId,
                operation,
                releaseOwnership,
                stopping.Token);
            if (pendingCompilations.TryGetValue(workspaceId, out superseded))
            {
                pendingCompilations[workspaceId] = item;
                disposeSuperseded = true;
            }
            else if (!compilationQueue.Writer.TryWrite(workspaceId))
            {
                item.Dispose();
                rejectionCode = WorkspaceOutcomeReasons.WorkspaceAdmissionRejected;
                return false;
            }
            else
            {
                pendingCompilations.Add(workspaceId, item);
                _ = latestCompilations.TryGetValue(workspaceId, out superseded);
                if (superseded is not null && superseded.WasPublishedUnderLock)
                {
                    superseded = null;
                }
            }

            latestCompilations[workspaceId] = item;
        }

        if (disposeSuperseded)
        {
            superseded!.Abandon();
        }
        else
        {
            superseded?.CancelSuperseded();
        }

        rejectionCode = null;
        return true;
    }

    internal Task<WorkspaceCommandOutcome> RunSessionAsync(
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceCommandOutcome>(
                Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
        }

        Task<WorkspaceCommandOutcome> completion;
        lock (gate)
        {
            if (isDisposed)
            {
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
            }

            if (reservedSessionItems >= sessionQueueCapacity)
            {
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected));
            }

            SessionWorkItem? item = SessionWorkItem.CreateCommand(
                operation,
                cancellationToken,
                stopping.Token);
            try
            {
                completion = item.Completion!.Task;
                if (!sessionQueue.Writer.TryWrite(item))
                {
                    return Task.FromResult<WorkspaceCommandOutcome>(
                        Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
                }

                item = null;
                reservedSessionItems++;
            }
            finally
            {
                item?.Dispose();
            }
        }

        return completion;
    }

    internal bool TryStartSessionContinuation(
        Func<SessionContinuation, CancellationToken, ValueTask<WorkspaceCommandOutcome>>
            operation,
        out string? rejectionCode)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            if (isDisposed)
            {
                rejectionCode = WorkspaceOutcomeReasons.WorkspaceCancelled;
                return false;
            }

            if (reservedSessionItems >= sessionQueueCapacity)
            {
                rejectionCode = WorkspaceOutcomeReasons.WorkspaceAdmissionRejected;
                return false;
            }

            var continuation = new SessionContinuation(this);
            SessionWorkItem? item = SessionWorkItem.CreateContinuation(
                token => operation(continuation, token),
                continuation,
                stopping.Token);
            try
            {
                if (!sessionQueue.Writer.TryWrite(item))
                {
                    rejectionCode = WorkspaceOutcomeReasons.WorkspaceCancelled;
                    return false;
                }

                item = null;
                continuation.MarkQueuedUnderLock();
                reservedSessionItems++;
                rejectionCode = null;
                return true;
            }
            finally
            {
                item?.Dispose();
            }
        }
    }

    private bool TryScheduleSessionContinuation(
        SessionContinuation continuation,
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            if (isDisposed || !continuation.CanScheduleUnderLock())
            {
                return false;
            }

            SessionWorkItem? item = SessionWorkItem.CreateContinuation(
                operation,
                continuation,
                stopping.Token);
            try
            {
                if (!sessionQueue.Writer.TryWrite(item))
                {
                    return false;
                }

                item = null;
                continuation.MarkQueuedUnderLock();
                return true;
            }
            finally
            {
                item?.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            compilationQueue.Writer.TryComplete();
            sessionQueue.Writer.TryComplete();
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(compilationWorker, sessionWorker).ConfigureAwait(false);
        stopping.Dispose();
    }

    private static Channel<T> CreateBoundedQueue<T>(int capacity)
    {
        return Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    private async Task ConsumeCompilationsAsync()
    {
        await foreach (var workspaceId in compilationQueue.Reader.ReadAllAsync()
            .ConfigureAwait(false))
        {
            CompilationWorkItem item;
            lock (gate)
            {
                item = pendingCompilations[workspaceId];
                _ = pendingCompilations.Remove(workspaceId);
            }

            try
            {
                var context = new CompilationWorkContext(
                    publication => TryApplyCompilationPublication(
                        item,
                        publication,
                        CompilationPublicationKind.Update),
                    publication => TryApplyCompilationPublication(
                        item,
                        publication,
                        CompilationPublicationKind.Publish),
                    publication => TryApplyCompilationPublication(
                        item,
                        publication,
                        CompilationPublicationKind.Reject),
                    item.CancellationToken);
                await item.Operation(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (ExceptionClassifier.IsCooperativeCancellation(
                    exception,
                    item.CancellationToken))
            {
                // Compilation terminal state is published by the workspace operation.
            }
            catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
            {
                ReportFailure(exception, "compilation");
            }
            finally
            {
                lock (gate)
                {
                    if (latestCompilations.TryGetValue(item.WorkspaceId, out var latest)
                        && ReferenceEquals(latest, item))
                    {
                        _ = latestCompilations.Remove(item.WorkspaceId);
                    }
                }

                item.ReleaseOwnership();
            }
        }
    }

    private async Task ConsumeSessionsAsync()
    {
        await foreach (var item in sessionQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (gate)
            {
                if (item.Continuation is null)
                {
                    reservedSessionItems--;
                }
                else
                {
                    item.Continuation.MarkExecutingUnderLock();
                }
            }

            try
            {
                var outcome = await item.Operation(item.CancellationToken).ConfigureAwait(false);
                item.Complete(outcome);
            }
            catch (OperationCanceledException exception)
                when (ExceptionClassifier.IsCooperativeCancellation(
                    exception,
                    item.CancellationToken))
            {
                item.Complete(
                    Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
            }
            catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
            {
                item.Complete(FailureOutcome(exception, "session"));
            }
            finally
            {
                if (item.Continuation is { } continuation)
                {
                    lock (gate)
                    {
                        if (continuation.CompleteExecutionUnderLock())
                        {
                            reservedSessionItems--;
                        }
                    }
                }

                item.Dispose();
            }
        }
    }

    private bool TryApplyCompilationPublication(
        CompilationWorkItem item,
        Action publication,
        CompilationPublicationKind kind)
    {
        lock (gate)
        {
            if (isDisposed
                || (kind != CompilationPublicationKind.Reject
                    && item.CancellationToken.IsCancellationRequested)
                || !latestCompilations.TryGetValue(item.WorkspaceId, out var latest)
                || !ReferenceEquals(latest, item))
            {
                return false;
            }

            publication();
            if (kind != CompilationPublicationKind.Update)
            {
                item.MarkPublishedUnderLock();
            }

            return true;
        }
    }

    private enum CompilationPublicationKind
    {
        Update,
        Publish,
        Reject,
    }

    private static WorkspaceCommandRejected Reject(string code)
    {
        return new WorkspaceCommandRejected(
            code,
            [],
            WorkspaceOutcomeReasons.RetryFor(code));
    }

    private WorkspaceCommandRejected FailureOutcome(Exception exception, string lane)
    {
        return Reject(ReportFailure(exception, lane));
    }

    private string ReportFailure(Exception exception, string lane)
    {
        var code = ExceptionClassifier.IsInfrastructureFailure(exception)
            ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
            : WorkspaceOutcomeReasons.WorkspaceInternalDefect;
        var correlation = Guid.CreateVersion7().ToString("N");
        LogWorkFailure(logger, exception, correlation, lane, code);
        return code;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Workspace work failed with correlation {Correlation}, lane {Lane}, and outcome {OutcomeCode}.")]
    private static partial void LogWorkFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string lane,
        string outcomeCode);

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

        protected WorkItem(CancellationToken stoppingToken)
            : this(
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
        }

        private WorkItem(CancellationTokenSource ownedCancellation)
        {
            cancellation = ownedCancellation;
            CancellationToken = cancellation.Token;
        }

        public CancellationToken CancellationToken { get; }

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
        CancellationToken stoppingToken)
        : WorkItem(stoppingToken)
    {
        private Action? ownershipRelease = releaseOwnership;

        public WorkspaceId WorkspaceId { get; } = workspaceId;

        public Func<CompilationWorkContext, ValueTask> Operation { get; }
            = operation;

        public bool WasPublishedUnderLock { get; private set; }

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
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
        SessionContinuation? continuation,
        TaskCompletionSource<WorkspaceCommandOutcome>? completion,
        CancellationToken callerCancellationToken,
        CancellationToken stoppingToken)
        : WorkItem(callerCancellationToken, stoppingToken)
    {
        public Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> Operation { get; }
            = operation;

        public SessionContinuation? Continuation { get; } = continuation;

        public TaskCompletionSource<WorkspaceCommandOutcome>? Completion { get; } = completion;

        public void Complete(WorkspaceCommandOutcome outcome)
        {
            Completion?.TrySetResult(outcome);
        }

        public static SessionWorkItem CreateCommand(
            Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
            CancellationToken callerCancellationToken,
            CancellationToken stoppingToken)
        {
            return new SessionWorkItem(
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
                operation,
                continuation,
                completion: null,
                CancellationToken.None,
                stoppingToken);
        }
    }

    internal sealed class SessionContinuation(WorkCoordinator owner)
    {
        private bool isExecuting;
        private bool isQueued;
        private bool isReserved = true;

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
    }
}

internal sealed class CompilationWorkContext(
    Func<Action, bool> update,
    Func<Action, bool> publish,
    Func<Action, bool> reject,
    CancellationToken cancellationToken)
{
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public bool TryUpdate(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return update(publication);
    }

    public bool TryPublish(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return publish(publication);
    }

    public bool TryReject(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return reject(publication);
    }
}
