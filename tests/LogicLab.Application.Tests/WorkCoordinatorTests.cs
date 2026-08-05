using LogicLab.Application.Work;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogicLab.Application.Tests;

internal sealed class WorkCoordinatorTests
{
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
}
