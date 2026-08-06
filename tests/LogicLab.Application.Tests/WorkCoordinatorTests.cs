using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed class WorkCoordinatorTests
{
    [Test, Timeout(30_000)]
    public async Task TryStartSessionContinuation_FullQueue_RejectsWithoutBypassingCapacity(
        CancellationToken cancellationToken)
    {
        await using var coordinator = new WorkCoordinator(
            new SchedulingPolicy(1, 1),
            NullLogger<WorkCoordinator>.Instance);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
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
        var continuationAdmitted = coordinator.TryStartSessionContinuation(
            new WorkspaceId("continuation"),
            (_, _) =>
            {
                executionOrder.Add("continuation");
                return ValueTask.FromResult<WorkspaceCommandOutcome>(TestOutcome());
            },
            out var rejectionCode);

        releaseFirst.TrySetResult();
        _ = await first.WaitAsync(cancellationToken);
        _ = await second.WaitAsync(cancellationToken);
        var rejection = await Assert.That(rejectedExternal)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);
        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
            await Assert.That(continuationAdmitted).IsFalse();
            await Assert.That(rejectionCode)
                .IsEqualTo(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
            await Assert.That(executionOrder).IsEquivalentTo([
                "first",
                "second",
            ], CollectionOrdering.Matching);
        }
    }

    [Test, Timeout(30_000)]
    public async Task TryStartSessionContinuation_ActiveReservation_RejectsExternalWorkAndContinues(
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
        var rescheduled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var admitted = coordinator.TryStartSessionContinuation(
            new WorkspaceId("run"),
            async (continuation, token) =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(token);
                rescheduled.TrySetResult(continuation.TrySchedule(
                    _ =>
                    {
                        continuationCompleted.TrySetResult();
                        return ValueTask.FromResult<WorkspaceCommandOutcome>(TestOutcome());
                    }));
                return TestOutcome();
            },
            out var rejectionCode);
        await firstStarted.Task.WaitAsync(cancellationToken);

        var rejectedExternal = await coordinator.RunSessionAsync(
            new WorkspaceId("external"),
            _ => ValueTask.FromResult<WorkspaceCommandOutcome>(TestOutcome()),
            cancellationToken);
        releaseFirst.TrySetResult();
        var wasRescheduled = await rescheduled.Task.WaitAsync(cancellationToken);
        await Assert.That(wasRescheduled).IsTrue();
        await continuationCompleted.Task.WaitAsync(cancellationToken);
        var rejection = await Assert.That(rejectedExternal)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

        using (Assert.Multiple())
        {
            await Assert.That(admitted).IsTrue();
            await Assert.That(rejectionCode).IsNull();
            await Assert.That(rejection.Code)
                .IsEqualTo(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
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
