using System.Diagnostics;
using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogicLab.Application.Tests;

internal sealed class WorkCoordinatorTests
{
    [Test]
    public async Task Schedule_PerSubjectFixedWindow_UsesMonotonicElapsedTime()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        var firstCaller = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("first"));
        var secondCaller = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("second"));
        await using var coordinator = new WorkCoordinator(
            Policy(admissionRequests: 1, admissionWindowMilliseconds: 1_000),
            timeProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkCoordinator>.Instance);

        var firstAccepted = Schedule(firstCaller, out var firstRejection);
        var firstRejected = Schedule(firstCaller, out var exhaustedRejection);
        var secondAccepted = Schedule(secondCaller, out var secondRejection);
        timeProvider.AdjustUtc(TimeSpan.FromDays(1));
        var afterForwardUtcJump = Schedule(firstCaller, out _);
        timeProvider.AdjustUtc(-TimeSpan.FromDays(2));
        var afterBackwardUtcJump = Schedule(firstCaller, out _);
        timeProvider.AdvanceTimestamp(TimeSpan.FromSeconds(1));
        var afterWindowAccepted = Schedule(firstCaller, out var resetRejection);

        using (Assert.Multiple())
        {
            await Assert.That(firstAccepted).IsTrue();
            await Assert.That(firstRejected).IsFalse();
            await Assert.That(secondAccepted).IsTrue();
            await Assert.That(afterForwardUtcJump).IsFalse();
            await Assert.That(afterBackwardUtcJump).IsFalse();
            await Assert.That(afterWindowAccepted).IsTrue();
            await Assert.That(firstRejection).IsNull();
            await Assert.That(secondRejection).IsNull();
            await Assert.That(resetRejection).IsNull();
            await Assert.That(exhaustedRejection?.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-scheduling",
                    "1",
                    "admission_requests_per_subject",
                    2));
        }

        bool Schedule(
            WorkspaceCaller caller,
            out WorkCoordinator.SchedulingRejection? rejection)
        {
            return coordinator.TryScheduleSession(
                new WorkspaceId(Guid.CreateVersion7().ToString("N")),
                caller,
                _ => ValueTask.FromResult<WorkspaceCommandOutcome>(
                    new WorkspaceCommandRejected(
                        "workspace_cancelled",
                        [],
                        RetryDisposition.DoNotRetry)),
                CancellationToken.None,
                out _,
                out rejection);
        }
    }

    [Test]
    public async Task Schedule_GlobalFixedWindow_RejectsIdentityChurn()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        await using var coordinator = new WorkCoordinator(
            Policy(
                admissionRequests: 8,
                admissionWindowMilliseconds: 1_000,
                admissionRequestsGlobal: 2,
                admissionPartitionCount: 8),
            timeProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkCoordinator>.Instance);

        var firstAccepted = Schedule("first", out _);
        var secondAccepted = Schedule("second", out _);
        var thirdAccepted = Schedule("third", out var rejection);

        using (Assert.Multiple())
        {
            await Assert.That(firstAccepted).IsTrue();
            await Assert.That(secondAccepted).IsTrue();
            await Assert.That(thirdAccepted).IsFalse();
            await Assert.That(rejection?.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-scheduling",
                    "1",
                    "admission_requests_global",
                    3));
        }

        bool Schedule(
            string subjectId,
            out WorkCoordinator.SchedulingRejection? rejection)
        {
            return coordinator.TryScheduleSession(
                new WorkspaceId(Guid.CreateVersion7().ToString("N")),
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId(subjectId)),
                _ => ValueTask.FromResult<WorkspaceCommandOutcome>(RejectedOutcome()),
                CancellationToken.None,
                out _,
                out rejection);
        }
    }

    [Test]
    public async Task Schedule_PartitionCapacity_RejectsChurnAndExpiresIdentityState()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        await using var coordinator = new WorkCoordinator(
            Policy(
                admissionRequests: 8,
                admissionWindowMilliseconds: 1_000,
                admissionRequestsGlobal: 3,
                admissionPartitionCount: 2),
            timeProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkCoordinator>.Instance);

        _ = Schedule("first", out _);
        _ = Schedule("second", out _);
        var exhausted = Schedule("third", out var rejection);
        var globallyExhausted = Schedule("fourth", out var globalRejection);
        timeProvider.AdvanceTimestamp(TimeSpan.FromSeconds(1));
        var afterExpiry = Schedule("third", out var afterExpiryRejection);

        using (Assert.Multiple())
        {
            await Assert.That(exhausted).IsFalse();
            await Assert.That(rejection?.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-scheduling",
                    "1",
                    "admission_partition_count",
                    3));
            await Assert.That(globallyExhausted).IsFalse();
            await Assert.That(globalRejection?.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-scheduling",
                    "1",
                    "admission_requests_global",
                    4));
            await Assert.That(afterExpiry).IsTrue();
            await Assert.That(afterExpiryRejection).IsNull();
        }

        bool Schedule(
            string subjectId,
            out WorkCoordinator.SchedulingRejection? rejection)
        {
            return coordinator.TryScheduleSession(
                new WorkspaceId(Guid.CreateVersion7().ToString("N")),
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId(subjectId)),
                _ => ValueTask.FromResult<WorkspaceCommandOutcome>(RejectedOutcome()),
                CancellationToken.None,
                out _,
                out rejection);
        }
    }

    [Test, Timeout(30_000)]
    public async Task Schedule_MultipleSessionWorkers_SerializeOneWorkspaceOnly(
        CancellationToken cancellationToken)
    {
        await using var coordinator = new WorkCoordinator(
            Policy(
                admissionRequests: 8,
                admissionWindowMilliseconds: 1_000,
                sessionWorkerCount: 2),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkCoordinator>.Instance);
        var firstWorkspace = new WorkspaceId("first-workspace");
        var secondWorkspace = new WorkspaceId("second-workspace");
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sameWorkspaceStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var otherWorkspaceStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _ = coordinator.TryScheduleSession(
            firstWorkspace,
            AnonymousWorkspaceCaller.Instance,
            async _ =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.ConfigureAwait(false);
                return RejectedOutcome();
            },
            cancellationToken,
            out var first,
            out _);
        try
        {
            await firstStarted.Task.WaitAsync(cancellationToken);
            _ = coordinator.TryScheduleSession(
                firstWorkspace,
                AnonymousWorkspaceCaller.Instance,
                _ =>
                {
                    sameWorkspaceStarted.TrySetResult();
                    return ValueTask.FromResult<WorkspaceCommandOutcome>(RejectedOutcome());
                },
                cancellationToken,
                out var sameWorkspace,
                out _);
            _ = coordinator.TryScheduleSession(
                secondWorkspace,
                AnonymousWorkspaceCaller.Instance,
                _ =>
                {
                    otherWorkspaceStarted.TrySetResult();
                    return ValueTask.FromResult<WorkspaceCommandOutcome>(RejectedOutcome());
                },
                cancellationToken,
                out var otherWorkspace,
                out _);

            await otherWorkspaceStarted.Task.WaitAsync(cancellationToken);
            await Assert.That(sameWorkspaceStarted.Task.IsCompleted).IsFalse();
            releaseFirst.TrySetResult();
            await sameWorkspaceStarted.Task.WaitAsync(cancellationToken);
            await Task.WhenAll(
                first!.Completion,
                sameWorkspace!.Completion,
                otherWorkspace!.Completion).WaitAsync(cancellationToken);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        static WorkspaceCommandRejected RejectedOutcome()
        {
            return new WorkspaceCommandRejected(
                "workspace_cancelled",
                [],
                RetryDisposition.DoNotRetry);
        }
    }

    [Test, Timeout(30_000)]
    [Arguments("compilation", false)]
    [Arguments("session", false)]
    [Arguments("compilation", true)]
    [Arguments("session", true)]
    public async Task Schedule_WorkItemFailure_UsesSchedulingTrace(
        string lane,
        bool collectTrace,
        CancellationToken cancellationToken)
    {
        using var logs = new FakeLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var coordinator = CreateUnderActivity(
            loggerFactory.CreateLogger<WorkCoordinator>(),
            out var constructionTrace);
        await using var coordinatorLifetime = coordinator;
        var schedulingTrace = ActivityTraceId.CreateRandom();
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => collectTrace && source.Name == WorkTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Parent.TraceId == schedulingTrace
                ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == schedulingTrace)
                {
                    stopped.TrySetResult(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var schedulingActivity = new Activity("scheduling-request");
        schedulingActivity.SetIdFormat(ActivityIdFormat.W3C);
        schedulingActivity.SetParentId(
            schedulingTrace,
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        schedulingActivity.Start();

        WorkCoordinator.SchedulingRejection? rejection;
        if (lane == "compilation")
        {
            var released = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var accepted = coordinator.TryScheduleCompilation(
                new WorkspaceId("correlation-compilation"),
                AnonymousWorkspaceCaller.Instance,
                _ => throw new InvalidOperationException("Compilation failed."),
                () => released.TrySetResult(),
                cancellationToken,
                out rejection);
            await Assert.That(accepted).IsTrue();
            await released.Task.WaitAsync(cancellationToken);
        }
        else
        {
            var accepted = coordinator.TryScheduleSession(
                new WorkspaceId("correlation-session"),
                AnonymousWorkspaceCaller.Instance,
                _ => throw new InvalidOperationException("Session failed."),
                cancellationToken,
                out var scheduled,
                out rejection);
            await Assert.That(accepted).IsTrue();
            _ = await scheduled!.Completion.WaitAsync(cancellationToken);
        }

        var log = logs.Collector.GetSnapshot().Single(entry => entry.Id.Id == 1001);
        if (collectTrace)
        {
            var activity = await stopped.Task.WaitAsync(cancellationToken);
            await Assert.That(activity.OperationName).IsEqualTo($"LogicLab.Work.{lane}");
            await Assert.That(activity.ParentSpanId).IsEqualTo(schedulingActivity.SpanId);
            await Assert.That(activity.TraceId).IsEqualTo(schedulingTrace);
            await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Internal);
            await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
            await Assert.That(activity.StatusDescription).IsEqualTo(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
            await Assert.That(activity.TagObjects).IsEmpty();
            await Assert.That(activity.Events).IsEmpty();
            await Assert.That(activity.Baggage).IsEmpty();
        }
        using (Assert.Multiple())
        {
            await Assert.That(rejection).IsNull();
            await Assert.That(log.GetStructuredStateValue("Lane")).IsEqualTo(lane);
            await Assert.That(log.Exception).IsNull();
            await Assert.That(log.Message).DoesNotContain("Compilation failed.");
            await Assert.That(log.Message).DoesNotContain("Session failed.");
            await Assert.That(log.GetStructuredStateValue("Correlation"))
                .IsEqualTo(schedulingTrace.ToHexString());
            await Assert.That(log.GetStructuredStateValue("Correlation"))
                .IsNotEqualTo(constructionTrace.ToHexString());
        }
    }

    [Test, Timeout(30_000)]
    public async Task DisposeAsync_CancellationCallbackFails_DrainsRunningWorkBeforeReportingFailure(
        CancellationToken cancellationToken)
    {
        var coordinator = new WorkCoordinator(
            SchedulingPolicy.Default,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkCoordinator>.Instance);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = coordinator.TryScheduleCompilation(
            new WorkspaceId("disposal-failure"),
            AnonymousWorkspaceCaller.Instance,
            async context =>
            {
                using var registration = context.CancellationToken.Register(() =>
                {
                    cancellationObserved.TrySetResult();
                    throw new InvalidOperationException("Cancellation callback failed.");
                });
                started.TrySetResult();
                await finish.Task.ConfigureAwait(false);
            },
            () => released.TrySetResult(),
            cancellationToken,
            out _);
        Task? disposal = null;
        try
        {
            await Assert.That(accepted).IsTrue();
            await started.Task.WaitAsync(cancellationToken);
            disposal = coordinator.DisposeAsync().AsTask();
            await cancellationObserved.Task.WaitAsync(cancellationToken);

            // The worker is deliberately held after cancellation: a callback failure
            // must not let disposal cross the dependency-lifetime boundary early.
            await Assert.That(() => disposal.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken))
                .ThrowsExactly<TimeoutException>();
            finish.TrySetResult();
            await Assert.That(() => disposal.WaitAsync(cancellationToken))
                .ThrowsExactly<AggregateException>();
            await Assert.That(released.Task.IsCompletedSuccessfully).IsTrue();
        }
        finally
        {
            finish.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            if (disposal is null)
            {
                await coordinator.DisposeAsync();
            }
        }
    }

    private static WorkCoordinator CreateUnderActivity(
        ILogger<WorkCoordinator> logger,
        out ActivityTraceId constructionTrace)
    {
        var parentTrace = ActivityTraceId.CreateRandom();
        using var activity = new Activity("coordinator-construction");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.SetParentId(
            parentTrace,
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        activity.Start();
        constructionTrace = activity.TraceId;
        return new WorkCoordinator(SchedulingPolicy.Default, TimeProvider.System, logger);
    }

    private static SchedulingPolicy Policy(
        ulong admissionRequests,
        ulong admissionWindowMilliseconds,
        ulong admissionRequestsGlobal = 10_000,
        ulong admissionPartitionCount = 1_000,
        ulong sessionWorkerCount = 1)
    {
        return new SchedulingPolicy(
            "test-scheduling",
            "1",
            [
                new(SchedulingDimension.AdmissionRequestsGlobal, admissionRequestsGlobal),
                new(SchedulingDimension.AdmissionRequestsPerSubject, admissionRequests),
                new(SchedulingDimension.AdmissionPartitionCount, admissionPartitionCount),
                new(SchedulingDimension.AdmissionWindowMilliseconds, admissionWindowMilliseconds),
                new(SchedulingDimension.CompilationQueueItems, 8),
                new(SchedulingDimension.SessionQueueItems, 8),
                new(SchedulingDimension.CompilationWorkerCount, 1),
                new(SchedulingDimension.SessionWorkerCount, sessionWorkerCount),
            ]);
    }

    private static WorkspaceCommandRejected RejectedOutcome()
    {
        return new WorkspaceCommandRejected(
            "workspace_cancelled",
            [],
            RetryDisposition.DoNotRetry);
    }
}
