using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Work;

internal sealed partial class WorkCoordinator : IAsyncDisposable
{
    private static readonly ActivitySource WorkActivitySource = new(
        "LogicLab.Application.Work");

    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly LinkedList<WorkspaceId> compilationQueue = [];
    private readonly SemaphoreSlim compilationQueueSignal = new(0);
    private readonly int compilationQueueCapacity;
    private readonly LinkedList<SessionWorkItem> sessionQueue = [];
    private readonly SemaphoreSlim sessionQueueSignal = new(0);
    private readonly HashSet<WorkspaceId> activeSessionWorkspaces = [];
    private readonly int sessionQueueCapacity;
    private readonly SchedulingPolicy policy;
    private readonly SchedulingAdmission schedulingAdmission;
    private readonly ILogger<WorkCoordinator> logger;
    private readonly Dictionary<WorkspaceId, CompilationWorkItem> latestCompilations = [];
    private readonly Dictionary<WorkspaceId, CompilationWorkItem> pendingCompilations = [];
    private readonly Task[] compilationWorkers;
    private readonly Task[] sessionWorkers;
    private int reservedSessionItems;
    private bool isDisposed;

    public WorkCoordinator(
        SchedulingPolicy policy,
        TimeProvider timeProvider,
        ILogger<WorkCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.policy = policy;
        this.logger = logger;
        schedulingAdmission = new SchedulingAdmission(policy, timeProvider);
        compilationQueueCapacity = policy.GetInt32Maximum(
            SchedulingDimension.CompilationQueueItems);
        sessionQueueCapacity = policy.GetInt32Maximum(
            SchedulingDimension.SessionQueueItems);
        (compilationWorkers, sessionWorkers) = StartWorkers(
            policy.GetInt32Maximum(SchedulingDimension.CompilationWorkerCount),
            policy.GetInt32Maximum(SchedulingDimension.SessionWorkerCount));
    }

    private (Task[] Compilation, Task[] Session) StartWorkers(
        int compilationWorkerCount,
        int sessionWorkerCount)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            return CreateWorkers();
        }

        using (ExecutionContext.SuppressFlow())
        {
            return CreateWorkers();
        }

        (Task[] Compilation, Task[] Session) CreateWorkers()
        {
            return (
                Enumerable.Range(0, compilationWorkerCount)
                    .Select(_ => ConsumeCompilationsAsync())
                    .ToArray(),
                Enumerable.Range(0, sessionWorkerCount)
                    .Select(_ => ConsumeSessionsAsync())
                    .ToArray());
        }
    }

    internal bool TryScheduleCompilation(
        WorkspaceId workspaceId,
        WorkspaceCaller caller,
        Func<CompilationWorkContext, ValueTask> operation,
        Action releaseOwnership,
        CancellationToken admissionCancellationToken,
        [NotNullWhen(false)] out SchedulingRejection? rejection)
    {
        return TryScheduleCompilation(
            workspaceId,
            caller,
            operation,
            releaseOwnership,
            CompilationWorkCancellation.OutlivesCaller,
            admissionCancellationToken,
            out _,
            out rejection);
    }

    internal bool TryScheduleCompilation(
        WorkspaceId workspaceId,
        WorkspaceCaller caller,
        Func<CompilationWorkContext, ValueTask> operation,
        Action releaseOwnership,
        CompilationWorkCancellation cancellationBehavior,
        CancellationToken admissionCancellationToken,
        [NotNullWhen(true)] out ScheduledCompilationWork? scheduledWork,
        [NotNullWhen(false)] out SchedulingRejection? rejection)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(releaseOwnership);
        var operationCancellationToken = cancellationBehavior switch
        {
            CompilationWorkCancellation.OutlivesCaller => CancellationToken.None,
            CompilationWorkCancellation.BoundToCaller => admissionCancellationToken,
            _ => throw new ArgumentOutOfRangeException(
                nameof(cancellationBehavior),
                cancellationBehavior,
                null),
        };
        CompilationWorkItem item;
        CompilationWorkItem? superseded = null;
        var disposeSuperseded = false;
        lock (gate)
        {
            if (isDisposed || admissionCancellationToken.IsCancellationRequested)
            {
                scheduledWork = null;
                rejection = CancelledRejection;
                return false;
            }

            if (!TryAdmitSchedulingUnderLock(caller, out rejection))
            {
                scheduledWork = null;
                return false;
            }

            item = new CompilationWorkItem(
                workspaceId,
                operation,
                releaseOwnership,
                operationCancellationToken,
                stopping.Token);
            if (pendingCompilations.TryGetValue(workspaceId, out superseded))
            {
                item.MarkQueuedUnderLock(superseded.QueueNode!);
                superseded.MarkRemovedUnderLock();
                pendingCompilations[workspaceId] = item;
                disposeSuperseded = true;
            }
            else if (compilationQueue.Count >= compilationQueueCapacity)
            {
                item.Dispose();
                scheduledWork = null;
                rejection = PolicyRejection(
                    SchedulingDimension.CompilationQueueItems,
                    checked((ulong)compilationQueue.Count + 1));
                return false;
            }
            else
            {
                EnqueueCompilationUnderLock(item);
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

        scheduledWork = new ScheduledCompilationWork(() => CancelCompilation(item));
        rejection = null;
        return true;
    }

    private void CancelCompilation(CompilationWorkItem item)
    {
        bool abandoned;
        lock (gate)
        {
            abandoned = pendingCompilations.TryGetValue(item.WorkspaceId, out var pending)
                && ReferenceEquals(pending, item);
            if (abandoned)
            {
                _ = pendingCompilations.Remove(item.WorkspaceId);
                RemoveQueuedCompilationUnderLock(item);
                if (latestCompilations.TryGetValue(item.WorkspaceId, out var latest)
                    && ReferenceEquals(latest, item))
                {
                    _ = latestCompilations.Remove(item.WorkspaceId);
                }
            }
        }

        if (abandoned)
        {
            item.Abandon();
        }
        else
        {
            item.CancelSuperseded();
        }
    }

    internal bool TryScheduleSession(
        WorkspaceId workspaceId,
        WorkspaceCaller caller,
        Func<CancellationToken, ValueTask<WorkspaceCommandOutcome>> operation,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out ScheduledSessionWork? scheduledWork,
        [NotNullWhen(false)] out SchedulingRejection? rejection)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
        {
            scheduledWork = null;
            rejection = CancelledRejection;
            return false;
        }

        lock (gate)
        {
            if (isDisposed)
            {
                scheduledWork = null;
                rejection = CancelledRejection;
                return false;
            }

            if (!TryAdmitSchedulingUnderLock(caller, out rejection))
            {
                scheduledWork = null;
                return false;
            }

            if (reservedSessionItems >= sessionQueueCapacity)
            {
                scheduledWork = null;
                rejection = PolicyRejection(
                    SchedulingDimension.SessionQueueItems,
                    checked((ulong)reservedSessionItems + 1));
                return false;
            }

            SessionWorkItem? item = SessionWorkItem.CreateCommand(
                workspaceId,
                operation,
                cancellationToken,
                stopping.Token);
            try
            {
                EnqueueSessionUnderLock(item);
                var scheduledItem = item;
                scheduledWork = new ScheduledSessionWork(
                    scheduledItem.Completion!.Task,
                    () => CancelSession(scheduledItem));
                item = null;
                reservedSessionItems++;
                rejection = null;
                return true;
            }
            finally
            {
                item?.Dispose();
            }
        }
    }

    private void CancelSession(SessionWorkItem item)
    {
        bool abandoned;
        lock (gate)
        {
            abandoned = item.IsQueuedUnderLock();
            if (abandoned)
            {
                RemoveQueuedSessionUnderLock(item);
                reservedSessionItems--;
            }
        }

        item.CancelScheduledWork();
        if (abandoned)
        {
            item.Complete(Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
            item.Dispose();
        }
    }

    internal bool TryStartSessionContinuation(
        WorkspaceId workspaceId,
        Func<SessionContinuation, CancellationToken, ValueTask<WorkspaceCommandOutcome>>
            operation,
        [NotNullWhen(false)] out SchedulingRejection? rejection)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            if (isDisposed)
            {
                rejection = CancelledRejection;
                return false;
            }

            if (reservedSessionItems >= sessionQueueCapacity)
            {
                rejection = PolicyRejection(
                    SchedulingDimension.SessionQueueItems,
                    checked((ulong)reservedSessionItems + 1));
                return false;
            }

            var continuation = new SessionContinuation(this, workspaceId);
            SessionWorkItem? item = SessionWorkItem.CreateContinuation(
                token => operation(continuation, token),
                continuation,
                stopping.Token);
            try
            {
                EnqueueSessionUnderLock(item);
                item = null;
                continuation.MarkQueuedUnderLock();
                reservedSessionItems++;
                rejection = null;
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
                EnqueueSessionUnderLock(item);
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
        CompilationWorkItem[] abandonedCompilations;
        SessionWorkItem[] abandoned;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            schedulingAdmission.ClearUnderLock();
            abandonedCompilations = [.. pendingCompilations.Values];
            pendingCompilations.Clear();
            compilationQueue.Clear();
            foreach (var item in abandonedCompilations)
            {
                item.MarkRemovedUnderLock();
            }

            latestCompilations.Clear();
            while (compilationQueueSignal.Wait(0))
            {
            }

            abandoned = [.. sessionQueue];
            sessionQueue.Clear();
            activeSessionWorkspaces.Clear();
            foreach (var item in abandoned)
            {
                item.MarkRemovedUnderLock();
                _ = sessionQueueSignal.Wait(0);
                if (item.Continuation is null)
                {
                    reservedSessionItems--;
                }
                else if (item.Continuation.AbandonUnderLock())
                {
                    reservedSessionItems--;
                }
            }
        }

        foreach (var item in abandonedCompilations)
        {
            item.Abandon();
        }

        foreach (var item in abandoned)
        {
            item.CancelScheduledWork();
            item.Complete(Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
            item.Dispose();
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll([.. compilationWorkers, .. sessionWorkers])
            .ConfigureAwait(false);
        compilationQueueSignal.Dispose();
        sessionQueueSignal.Dispose();
        stopping.Dispose();
    }

    private void EnqueueCompilationUnderLock(CompilationWorkItem item)
    {
        item.MarkQueuedUnderLock(compilationQueue.AddLast(item.WorkspaceId));
        compilationQueueSignal.Release();
    }

    private bool TryAdmitSchedulingUnderLock(
        WorkspaceCaller caller,
        [NotNullWhen(false)] out SchedulingRejection? rejection)
    {
        if (!schedulingAdmission.TryAdmitUnderLock(
                caller,
                out var rejectionEvidence))
        {
            rejection = new SchedulingRejection(
                WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                rejectionEvidence);
            return false;
        }

        rejection = null;
        return true;
    }

    private SchedulingRejection PolicyRejection(
        SchedulingDimension dimension,
        ulong observed)
    {
        return new SchedulingRejection(
            WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
            policy.Evidence(dimension, observed));
    }

    private static SchedulingRejection CancelledRejection { get; } = new(
        WorkspaceOutcomeReasons.WorkspaceCancelled,
        PolicyEvidence: null);

    internal sealed record SchedulingRejection(
        string Code,
        PolicyEvidenceProjection? PolicyEvidence);

    private void RemoveQueuedCompilationUnderLock(CompilationWorkItem item)
    {
        compilationQueue.Remove(item.QueueNode!);
        item.MarkRemovedUnderLock();
        _ = compilationQueueSignal.Wait(0);
    }

    private void EnqueueSessionUnderLock(SessionWorkItem item)
    {
        item.MarkQueuedUnderLock(sessionQueue.AddLast(item));
        sessionQueueSignal.Release();
    }

    private void RemoveQueuedSessionUnderLock(SessionWorkItem item)
    {
        sessionQueue.Remove(item.QueueNode!);
        item.MarkRemovedUnderLock();
        _ = sessionQueueSignal.Wait(0);
    }

    private async Task ConsumeCompilationsAsync()
    {
        while (true)
        {
            try
            {
                await compilationQueueSignal.WaitAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (ExceptionClassifier.IsCooperativeCancellation(
                    exception,
                    stopping.Token))
            {
                return;
            }

            CompilationWorkItem item;
            lock (gate)
            {
                if (compilationQueue.First is null)
                {
                    continue;
                }

                var workspaceId = compilationQueue.First.Value;
                compilationQueue.RemoveFirst();
                item = pendingCompilations[workspaceId];
                _ = pendingCompilations.Remove(workspaceId);
                item.MarkRemovedUnderLock();
            }

            using var correlationScope = ApplicationCorrelation.Push(item.Correlation);
            using var activity = StartWorkActivity(item, "compilation");
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
        while (true)
        {
            try
            {
                await sessionQueueSignal.WaitAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (ExceptionClassifier.IsCooperativeCancellation(
                    exception,
                    stopping.Token))
            {
                return;
            }

            SessionWorkItem? item;
            lock (gate)
            {
                if (sessionQueue.First is null)
                {
                    continue;
                }

                var node = sessionQueue.First;
                while (node is not null
                    && activeSessionWorkspaces.Contains(node.Value.WorkspaceId))
                {
                    node = node.Next;
                }

                if (node is null)
                {
                    continue;
                }

                item = node.Value;
                sessionQueue.Remove(node);
                item.MarkRemovedUnderLock();
                activeSessionWorkspaces.Add(item.WorkspaceId);
                if (item.Continuation is null)
                {
                    reservedSessionItems--;
                }
                else
                {
                    item.Continuation.MarkExecutingUnderLock();
                }
            }

            using var correlationScope = ApplicationCorrelation.Push(item.Correlation);
            using var activity = StartWorkActivity(item, "session");
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
                lock (gate)
                {
                    _ = activeSessionWorkspaces.Remove(item.WorkspaceId);
                    if (item.Continuation is { } continuation
                        && continuation.CompleteExecutionUnderLock())
                    {
                        reservedSessionItems--;
                    }

                    if (sessionQueue.Any(queued =>
                            !activeSessionWorkspaces.Contains(queued.WorkspaceId)))
                    {
                        sessionQueueSignal.Release();
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
        var correlation = ApplicationCorrelation.CurrentOrCreate();
        LogWorkFailure(logger, exception, correlation, lane, code);
        return code;
    }

    private static Activity? StartWorkActivity(WorkItem item, string lane)
    {
        if (item.ParentActivityContext is not { } parent)
        {
            return null;
        }

        return WorkActivitySource.StartActivity(
            $"LogicLab.Work.{lane}",
            ActivityKind.Internal,
            parent);
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

}

internal enum CompilationWorkCancellation
{
    OutlivesCaller,
    BoundToCaller,
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
