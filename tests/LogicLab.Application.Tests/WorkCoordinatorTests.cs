using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

public sealed class WorkCoordinatorTests
{
    [Test]
    public async Task RunSessionAsync_AfterDisposal_ReturnsCancellationRejection()
    {
        var coordinator = new WorkCoordinator();
        await coordinator.DisposeAsync();

        var outcome = await coordinator.RunSessionAsync(
            WorkspaceId.Create(),
            _ => ValueTask.FromResult<WorkspaceCommandOutcome>(
                Completed("unexpected_execution")),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(((WorkspaceCommandRejected)outcome).Code)
            .IsEqualTo("workspace_cancelled");
    }

    [Test]
    public async Task RunSessionAsync_QueueIsFull_RejectsAdditionalWork()
    {
        await using var coordinator = new WorkCoordinator(
            compilationQueueCapacity: 1,
            sessionQueueCapacity: 1);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionOrder = new List<int>();

        var first = coordinator.RunSessionAsync(
            WorkspaceId.Create(),
            async cancellationToken =>
            {
                executionOrder.Add(1);
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
                return Completed("first_completed");
            },
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = coordinator.RunSessionAsync(
            WorkspaceId.Create(),
            _ =>
            {
                executionOrder.Add(2);
                return ValueTask.FromResult<WorkspaceCommandOutcome>(
                    Completed("second_completed"));
            },
            CancellationToken.None);
        var rejected = await coordinator.RunSessionAsync(
            WorkspaceId.Create(),
            _ => ValueTask.FromResult<WorkspaceCommandOutcome>(
                Completed("unexpected_execution")),
            CancellationToken.None);

        await Assert.That(rejected).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(((WorkspaceCommandRejected)rejected).Code)
            .IsEqualTo("workspace_admission_rejected");
        await Assert.That(executionOrder)
            .IsEquivalentTo([1], CollectionOrdering.Matching);

        releaseFirst.SetResult();
        _ = await first;
        _ = await second;
        await Assert.That(executionOrder)
            .IsEquivalentTo([1, 2], CollectionOrdering.Matching);
    }

    [Test]
    public async Task RunCompilationAsync_NewerRequest_SupersedesNonCooperativeOlderPublication()
    {
        await using var coordinator = new WorkCoordinator(
            compilationQueueCapacity: 2,
            sessionQueueCapacity: 1);
        var workspaceId = WorkspaceId.Create();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPublished = false;
        var secondPublished = false;

        var first = coordinator.RunCompilationAsync(
            workspaceId,
            async context =>
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
                _ = context.TryPublish(() => firstPublished = true);
                return Completed("first_completed");
            },
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = coordinator.RunCompilationAsync(
            workspaceId,
            context =>
            {
                var published = context.TryPublish(() => secondPublished = true);
                return ValueTask.FromResult<WorkspaceCommandOutcome>(published
                    ? Completed("second_completed")
                    : Completed("second_not_published"));
            },
            CancellationToken.None);

        releaseFirst.SetResult();
        var firstOutcome = await first;
        var secondOutcome = await second;

        await Assert.That(firstOutcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)firstOutcome).Code)
                .IsEqualTo("workspace_cancelled");
            await Assert.That(secondOutcome).IsEqualTo(Completed("second_completed"));
            await Assert.That(firstPublished).IsFalse();
            await Assert.That(secondPublished).IsTrue();
        }
    }

    private static WorkspaceCommandRejected Completed(string code)
    {
        return new WorkspaceCommandRejected(code, []);
    }
}
