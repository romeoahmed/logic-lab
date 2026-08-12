using System.Diagnostics;
using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Tests;

internal sealed class WorkCoordinatorTests
{
    [Test]
    public async Task Schedule_PerSubjectFixedWindow_RejectsOnlyExhaustedSubject()
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
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var afterWindowAccepted = Schedule(firstCaller, out var resetRejection);

        using (Assert.Multiple())
        {
            await Assert.That(firstAccepted).IsTrue();
            await Assert.That(firstRejected).IsFalse();
            await Assert.That(secondAccepted).IsTrue();
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

        static WorkspaceCommandRejected RejectedOutcome()
        {
            return new WorkspaceCommandRejected(
                "workspace_cancelled",
                [],
                RetryDisposition.DoNotRetry);
        }
    }

    [Test]
    [Arguments("compilation")]
    [Arguments("session")]
    public async Task Schedule_WorkItemFailure_UsesSchedulingTrace(string lane)
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var coordinator = CreateUnderActivity(
            loggerFactory.CreateLogger<WorkCoordinator>(),
            out var constructionTrace);
        await using var coordinatorLifetime = coordinator;
        var schedulingTrace = ActivityTraceId.CreateRandom();
        using var schedulingActivity = new Activity("scheduling-request")
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(
                schedulingTrace,
                ActivitySpanId.CreateRandom(),
                ActivityTraceFlags.Recorded)
            .Start();

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
                CancellationToken.None,
                out rejection);
            await Assert.That(accepted).IsTrue();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            var accepted = coordinator.TryScheduleSession(
                new WorkspaceId("correlation-session"),
                AnonymousWorkspaceCaller.Instance,
                _ => throw new InvalidOperationException("Session failed."),
                CancellationToken.None,
                out var scheduled,
                out rejection);
            await Assert.That(accepted).IsTrue();
            _ = await scheduled!.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var log = loggerFactory.Entries.Single(entry => entry.EventId.Id == 1001);
        using (Assert.Multiple())
        {
            await Assert.That(rejection).IsNull();
            await Assert.That(log.Properties["Lane"]).IsEqualTo(lane);
            await Assert.That(log.Properties["Correlation"])
                .IsEqualTo(schedulingTrace.ToHexString());
            await Assert.That(log.Properties["Correlation"])
                .IsNotEqualTo(constructionTrace.ToHexString());
        }
    }

    private static WorkCoordinator CreateUnderActivity(
        ILogger<WorkCoordinator> logger,
        out ActivityTraceId constructionTrace)
    {
        var parentTrace = ActivityTraceId.CreateRandom();
        using var activity = new Activity("coordinator-construction")
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(
                parentTrace,
                ActivitySpanId.CreateRandom(),
                ActivityTraceFlags.Recorded)
            .Start();
        constructionTrace = activity.TraceId;
        return new WorkCoordinator(SchedulingPolicy.Default, TimeProvider.System, logger);
    }

    private static SchedulingPolicy Policy(
        ulong admissionRequests,
        ulong admissionWindowMilliseconds,
        ulong sessionWorkerCount = 1)
    {
        return new SchedulingPolicy(
            "test-scheduling",
            "1",
            [
                new(SchedulingDimension.AdmissionRequestsPerSubject, admissionRequests),
                new(SchedulingDimension.AdmissionWindowMilliseconds, admissionWindowMilliseconds),
                new(SchedulingDimension.CompilationQueueItems, 8),
                new(SchedulingDimension.SessionQueueItems, 8),
                new(SchedulingDimension.AnalysisQueueItems, 8),
                new(SchedulingDimension.AnalysisQueueItemsPerSubject, 4),
                new(SchedulingDimension.CompilationWorkerCount, 1),
                new(SchedulingDimension.SessionWorkerCount, sessionWorkerCount),
                new(SchedulingDimension.AnalysisWorkerCount, 1),
                new(SchedulingDimension.AnalysisResultRetentionSeconds, 60),
            ]);
    }
}
