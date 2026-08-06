using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed class WorkCoordinatorTests
{
    [Test, Timeout(30_000)]
    public async Task TryScheduleSession_FullExternalQueue_AdmitsContinuationInFifoOrder(
        CancellationToken cancellationToken)
    {
        await using var coordinator = new WorkCoordinator(
            new SchedulingPolicy(1, 1),
            NullLogger<WorkCoordinator>.Instance);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continuationCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionOrder = new List<string>();

        var first = coordinator.RunSessionAsync(
            new WorkspaceId("first"),
            async token =>
            {
                executionOrder.Add("first");
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(token);
                return TestOutcome();
            },
            cancellationToken);
        await firstStarted.Task.WaitAsync(cancellationToken);
        var second = coordinator.RunSessionAsync(
            new WorkspaceId("second"),
            _ =>
            {
                executionOrder.Add("second");
                return ValueTask.FromResult<WorkspaceCommandOutcome>(TestOutcome());
            },
            cancellationToken);
        var rejectedExternal = await coordinator.RunSessionAsync(
            new WorkspaceId("rejected"),
            _ => ValueTask.FromResult<WorkspaceCommandOutcome>(TestOutcome()),
            cancellationToken);
        var continuationAdmitted = coordinator.TryScheduleSessionContinuation(
            new WorkspaceId("continuation"),
            _ =>
            {
                executionOrder.Add("continuation");
                continuationCompleted.TrySetResult();
                return ValueTask.FromResult<WorkspaceCommandOutcome>(TestOutcome());
            });

        releaseFirst.TrySetResult();
        _ = await first.WaitAsync(cancellationToken);
        _ = await second.WaitAsync(cancellationToken);
        var rejection = await Assert.That(rejectedExternal)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);
        await Assert.That(continuationAdmitted).IsTrue();
        await continuationCompleted.Task.WaitAsync(cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
            await Assert.That(executionOrder).IsEquivalentTo([
                "first",
                "second",
                "continuation",
            ], CollectionOrdering.Matching);
        }
    }

    [Test, Timeout(30_000)]
    public async Task RunSessionAsync_UnrelatedCancellationException_ReportsInternalDefect(
        CancellationToken cancellationToken)
    {
        await using var coordinator = new WorkCoordinator(
            new SchedulingPolicy(1, 1),
            NullLogger<WorkCoordinator>.Instance);
        using var callerCancellation = new CancellationTokenSource();
        using var unrelatedCancellation = new CancellationTokenSource();

        var outcome = await coordinator.RunSessionAsync(
                new WorkspaceId("workspace"),
                _ =>
                {
                    callerCancellation.Cancel();
                    unrelatedCancellation.Cancel();
                    return ValueTask.FromException<WorkspaceCommandOutcome>(
                        new OperationCanceledException(unrelatedCancellation.Token));
                },
                callerCancellation.Token)
            .WaitAsync(cancellationToken);

        var rejected = await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        await Assert.That(rejected.Code)
            .IsEqualTo(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
    }

    private static WorkspaceCommandRejected TestOutcome()
    {
        return new WorkspaceCommandRejected(
            "test_outcome",
            [],
            RetryDisposition.DoNotRetry);
    }
}
