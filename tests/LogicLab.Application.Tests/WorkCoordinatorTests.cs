using System.Diagnostics;
using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Tests;

internal sealed class WorkCoordinatorTests
{
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

        string? rejectionCode;
        if (lane == "compilation")
        {
            var released = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var accepted = coordinator.TryScheduleCompilation(
                new WorkspaceId("correlation-compilation"),
                _ => throw new InvalidOperationException("Compilation failed."),
                () => released.TrySetResult(),
                CancellationToken.None,
                out rejectionCode);
            await Assert.That(accepted).IsTrue();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            var accepted = coordinator.TryScheduleSession(
                _ => throw new InvalidOperationException("Session failed."),
                CancellationToken.None,
                out var scheduled,
                out rejectionCode);
            await Assert.That(accepted).IsTrue();
            _ = await scheduled!.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var log = loggerFactory.Entries.Single(entry => entry.EventId.Id == 1001);
        using (Assert.Multiple())
        {
            await Assert.That(rejectionCode).IsNull();
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
        return new WorkCoordinator(SchedulingPolicy.Default, logger);
    }
}
