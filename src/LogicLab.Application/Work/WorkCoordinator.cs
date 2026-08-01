using System.Threading.Channels;
using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Work;

public sealed class WorkCoordinator : IAsyncDisposable
{
    private const int DefaultCompilationQueueCapacity = 16;
    private const int DefaultSessionQueueCapacity = 64;

    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Channel<CompilationWorkItem> compilationQueue;
    private readonly Channel<SessionWorkItem> sessionQueue;
    private readonly Dictionary<WorkspaceId, CompilationWorkItem> latestCompilations = [];
    private readonly Task compilationWorker;
    private readonly Task sessionWorker;
    private bool isDisposed;

    public WorkCoordinator()
        : this(DefaultCompilationQueueCapacity, DefaultSessionQueueCapacity)
    {
    }

    internal WorkCoordinator(int compilationQueueCapacity, int sessionQueueCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(compilationQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionQueueCapacity);

        compilationQueue = CreateQueue<CompilationWorkItem>(compilationQueueCapacity);
        sessionQueue = CreateQueue<SessionWorkItem>(sessionQueueCapacity);
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
            return Task.FromResult<WorkspaceCommandOutcome>(Reject("operation_cancelled"));
        }

        CompilationWorkItem item;
        CompilationWorkItem? previous = null;
        lock (gate)
        {
            if (isDisposed)
            {
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject("work_coordinator_stopping"));
            }

            item = new CompilationWorkItem(
                workspaceId,
                operation,
                cancellationToken,
                stopping.Token);
            if (!compilationQueue.Writer.TryWrite(item))
            {
                item.Dispose();
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject("compilation_queue_full"));
            }

            _ = latestCompilations.TryGetValue(workspaceId, out previous);
            latestCompilations[workspaceId] = item;
        }

        previous?.Supersede();
        return item.Completion.Task;
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
            return Task.FromResult<WorkspaceCommandOutcome>(Reject("operation_cancelled"));
        }

        SessionWorkItem item;
        lock (gate)
        {
            if (isDisposed)
            {
                return Task.FromResult<WorkspaceCommandOutcome>(
                    Reject("work_coordinator_stopping"));
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
                    Reject("session_queue_full"));
            }
        }

        return item.Completion.Task;
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

        stopping.Cancel();
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
                    publication => TryPublish(item, publication),
                    item.CancellationToken);
                var outcome = await item.Operation(context).ConfigureAwait(false);
                item.Completion.TrySetResult(
                    item.CancellationToken.IsCancellationRequested && !item.WasPublished
                        ? CancellationOutcome(item)
                        : outcome);
            }
            catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetResult(CancellationOutcome(item));
            }
            catch (Exception exception)
            {
                item.Completion.TrySetException(exception);
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
            catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetResult(item.CallerCancellationToken.IsCancellationRequested
                    ? Reject("operation_cancelled")
                    : Reject("work_coordinator_stopping"));
            }
            catch (Exception exception)
            {
                item.Completion.TrySetException(exception);
            }
            finally
            {
                item.Dispose();
            }
        }
    }

    private bool TryPublish(CompilationWorkItem item, Action publication)
    {
        lock (gate)
        {
            if (isDisposed
                || item.CancellationToken.IsCancellationRequested
                || !latestCompilations.TryGetValue(item.WorkspaceId, out var latest)
                || !ReferenceEquals(latest, item))
            {
                return false;
            }

            publication();
            item.MarkPublished();
            return true;
        }
    }

    private static WorkspaceCommandRejected CancellationOutcome(CompilationWorkItem item)
    {
        if (item.IsSuperseded)
        {
            return Reject("compilation_superseded");
        }

        return item.CallerCancellationToken.IsCancellationRequested
            ? Reject("operation_cancelled")
            : Reject("work_coordinator_stopping");
    }

    private static WorkspaceCommandRejected Reject(string code)
    {
        return new WorkspaceCommandRejected(code, []);
    }

    private abstract class WorkItem : IDisposable
    {
        private readonly CancellationTokenSource cancellation;

        protected WorkItem(
            WorkspaceId workspaceId,
            CancellationToken callerCancellationToken,
            CancellationToken stoppingToken)
        {
            WorkspaceId = workspaceId;
            CallerCancellationToken = callerCancellationToken;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellationToken,
                stoppingToken);
        }

        public WorkspaceId WorkspaceId { get; }

        public CancellationToken CallerCancellationToken { get; }

        public CancellationToken CancellationToken => cancellation.Token;

        public TaskCompletionSource<WorkspaceCommandOutcome> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected void Cancel() => cancellation.Cancel();

        public void Dispose() => cancellation.Dispose();
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

        public void Supersede()
        {
            if (WasPublished)
            {
                return;
            }

            Volatile.Write(ref isSuperseded, 1);
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
    Func<Action, bool> publish,
    CancellationToken cancellationToken)
{
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public bool TryPublish(Action publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return publish(publication);
    }
}
