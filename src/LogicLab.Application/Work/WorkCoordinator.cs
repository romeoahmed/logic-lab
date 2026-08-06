using System.Threading.Channels;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Work;

internal sealed partial class WorkCoordinator : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Channel<CompilationWorkItem> compilationQueue;
    private readonly Channel<SessionWorkItem> sessionQueue;
    private readonly ILogger<WorkCoordinator> logger;
    private readonly Dictionary<WorkspaceId, CompilationWorkItem> latestCompilations = [];
    private readonly Task compilationWorker;
    private readonly Task sessionWorker;
    private bool isDisposed;

    public WorkCoordinator(
        SchedulingPolicy policy,
        ILogger<WorkCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        compilationQueue = CreateQueue<CompilationWorkItem>(policy.CompilationQueueCapacity);
        sessionQueue = CreateQueue<SessionWorkItem>(policy.SessionQueueCapacity);
        compilationWorker = ConsumeCompilationsAsync();
        sessionWorker = ConsumeSessionsAsync();
    }

    internal Task<WorkspaceCommandOutcome> RunCompilationAsync(
        WorkspaceId workspaceId,
        Func<CompilationWorkContext, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceCommandOutcome>(
                Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
        }

        _ = TryEnqueueCompilation(
            workspaceId,
            operation,
            cancellationToken,
            out var completion);
        return completion;
    }

    internal bool TryScheduleCompilation(
        WorkspaceId workspaceId,
        Func<CompilationWorkContext, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(operation);
        return TryEnqueueCompilation(
            workspaceId,
            operation,
            cancellationToken,
            out _);
    }

    private bool TryEnqueueCompilation(
        WorkspaceId workspaceId,
        Func<CompilationWorkContext, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken cancellationToken,
        out Task<WorkspaceCommandOutcome> completion)
    {
        CompilationWorkItem item;
        CompilationWorkItem? previous = null;
        lock (gate)
        {
            if (isDisposed || cancellationToken.IsCancellationRequested)
            {
                completion = Task.FromResult<WorkspaceCommandOutcome>(
                    Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
                return false;
            }

            item = new CompilationWorkItem(
                workspaceId,
                operation,
                cancellationToken,
                stopping.Token);
            if (!compilationQueue.Writer.TryWrite(item))
            {
                item.Dispose();
                completion = Task.FromResult<WorkspaceCommandOutcome>(
                    Reject(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected));
                return false;
            }

            _ = latestCompilations.TryGetValue(workspaceId, out previous);
            latestCompilations[workspaceId] = item;
            if (previous is not null && !previous.TryMarkSuperseded())
            {
                previous = null;
            }
        }

        previous?.CancelSuperseded();
        completion = item.Completion.Task;
        return true;
    }

    internal Task<WorkspaceCommandOutcome> RunSessionAsync(
        WorkspaceId workspaceId,
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceCommandOutcome>(
                Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
        }

        SessionWorkItem item;
        lock (gate)
        {
            if (isDisposed)
            {
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
            }

            item = new SessionWorkItem(
                workspaceId,
                operation,
                cancellationToken,
                stopping.Token);
            if (!sessionQueue.Writer.TryWrite(item))
            {
                item.Dispose();
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected));
            }
        }

        return item.Completion.Task;
    }

    internal bool TryScheduleSession(
        WorkspaceId workspaceId,
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            if (isDisposed)
            {
                return false;
            }

            var item = new SessionWorkItem(
                workspaceId,
                operation,
                CancellationToken.None,
                stopping.Token);
            if (sessionQueue.Writer.TryWrite(item))
            {
                return true;
            }

            item.Dispose();
            return false;
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

    private static Channel<T> CreateQueue<T>(int capacity)
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
        await foreach (var item in compilationQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var context = new CompilationWorkContext(
                    publication => TryUpdate(
                        item,
                        publication,
                        terminal: false,
                        allowCancellation: false),
                    publication => TryUpdate(
                        item,
                        publication,
                        terminal: true,
                        allowCancellation: false),
                    publication => TryUpdate(
                        item,
                        publication,
                        terminal: true,
                        allowCancellation: true),
                    item.CancellationToken);
                var outcome = await item.Operation(context).ConfigureAwait(false);
                item.Completion.TrySetResult(
                    (item.IsSuperseded || item.CancellationToken.IsCancellationRequested)
                    && !item.WasPublished
                        ? CancellationOutcome()
                        : outcome);
            }
            catch (OperationCanceledException exception)
                when (ExceptionClassifier.IsCooperativeCancellation(
                    exception,
                    item.CancellationToken))
            {
                item.Completion.TrySetResult(CancellationOutcome());
            }
            catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
            {
                item.Completion.TrySetResult(FailureOutcome(exception, "compilation"));
            }
            finally
            {
                lock (gate)
                {
                    if (latestCompilations.TryGetValue(item.WorkspaceId, out var latest)
                        && ReferenceEquals(latest, item))
                    {
                        latestCompilations.Remove(item.WorkspaceId);
                    }
                }

                item.Dispose();
            }
        }
    }

    private async Task ConsumeSessionsAsync()
    {
        await foreach (var item in sessionQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var outcome = await item.Operation(item.CancellationToken).ConfigureAwait(false);
                item.Completion.TrySetResult(outcome);
            }
            catch (OperationCanceledException exception)
                when (ExceptionClassifier.IsCooperativeCancellation(
                    exception,
                    item.CancellationToken))
            {
                item.Completion.TrySetResult(
                    Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
            }
            catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
            {
                item.Completion.TrySetResult(FailureOutcome(exception, "session"));
            }
            finally
            {
                item.Dispose();
            }
        }
    }

    private bool TryUpdate(
        CompilationWorkItem item,
        Action publication,
        bool terminal,
        bool allowCancellation)
    {
        lock (gate)
        {
            if (isDisposed
                || (!allowCancellation && item.CancellationToken.IsCancellationRequested)
                || !latestCompilations.TryGetValue(item.WorkspaceId, out var latest)
                || !ReferenceEquals(latest, item))
            {
                return false;
            }

            publication();
            if (terminal)
            {
                item.MarkPublished();
            }

            return true;
        }
    }

    private static WorkspaceCommandRejected CancellationOutcome()
    {
        return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
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
        var code = exception is IOException or TimeoutException
            ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
            : WorkspaceOutcomeReasons.WorkspaceInternalDefect;
        var correlation = Guid.CreateVersion7().ToString("N");
        LogWorkFailure(logger, exception, correlation, lane, code);
        return Reject(code);
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
            WorkspaceId workspaceId,
            CancellationToken callerCancellationToken,
            CancellationToken stoppingToken)
        {
            WorkspaceId = workspaceId;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellationToken,
                stoppingToken);
            CancellationToken = cancellation.Token;
        }

        public WorkspaceId WorkspaceId { get; }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource<WorkspaceCommandOutcome> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

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
        Func<CompilationWorkContext, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken callerCancellationToken,
        CancellationToken stoppingToken)
        : WorkItem(workspaceId, callerCancellationToken, stoppingToken)
    {
        private int isSuperseded;
        private int wasPublished;

        public Func<CompilationWorkContext, ValueTask<WorkspaceCommandOutcome>> Operation { get; }
            = operation;

        public bool IsSuperseded => Volatile.Read(ref isSuperseded) != 0;

        public bool WasPublished => Volatile.Read(ref wasPublished) != 0;

        public bool TryMarkSuperseded()
        {
            if (WasPublished)
            {
                return false;
            }

            Volatile.Write(ref isSuperseded, 1);
            return true;
        }

        public void CancelSuperseded()
        {
            Cancel();
        }

        public void MarkPublished() => Volatile.Write(ref wasPublished, 1);
    }

    private sealed class SessionWorkItem(
        WorkspaceId workspaceId,
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken callerCancellationToken,
        CancellationToken stoppingToken)
        : WorkItem(workspaceId, callerCancellationToken, stoppingToken)
    {
        public Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> Operation { get; }
            = operation;
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
