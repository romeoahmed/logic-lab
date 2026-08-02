using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceSchedulingTests
{
    [Test]
    public async Task DispatchAsync_CompilationQueueFull_RejectsThroughWorkspaceBoundary()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, cancellationToken) =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    firstStarted.TrySetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                }

                return Compiler.Compile(request, cancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(1, 1),
            operations: operations);
        var firstWorkspace = (WorkspaceOpened)await Open(workspace, "First");
        var secondWorkspace = (WorkspaceOpened)await Open(workspace, "Second");
        var thirdWorkspace = (WorkspaceOpened)await Open(workspace, "Third");

        var first = workspace.DispatchAsync(
            new RequestCompilation(firstWorkspace.WorkspaceId),
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = workspace.DispatchAsync(
            new RequestCompilation(secondWorkspace.WorkspaceId),
            CancellationToken.None);
        var rejected = await workspace.DispatchAsync(
            new RequestCompilation(thirdWorkspace.WorkspaceId),
            CancellationToken.None);
        releaseFirst.TrySetResult();
        _ = await first;
        _ = await second;

        using (Assert.Multiple())
        {
            await Assert.That(rejected).IsTypeOf<WorkspaceCommandRejected>();
            await Assert.That(((WorkspaceCommandRejected)rejected).Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task DispatchAsync_NewerCompilation_SupersedesOlderPublication()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, cancellationToken) =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    firstStarted.TrySetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                }

                return Compiler.Compile(request, cancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(2, 1),
            operations: operations);
        var opened = (WorkspaceOpened)await Open(workspace, "Newest wins");

        var first = workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        releaseFirst.TrySetResult();

        var firstOutcome = await first;
        var secondOutcome = await second;

        using (Assert.Multiple())
        {
            await Assert.That(firstOutcome).IsTypeOf<WorkspaceCommandRejected>();
            await Assert.That(((WorkspaceCommandRejected)firstOutcome).Code)
                .IsEqualTo("workspace_cancelled");
            await Assert.That(secondOutcome).IsTypeOf<CompilationPublished>();
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task DispatchAsync_CloseDuringCompilation_PreventsLatePublication()
    {
        var compilationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCompilation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, cancellationToken) =>
            {
                compilationStarted.TrySetResult();
                releaseCompilation.Task.GetAwaiter().GetResult();
                return Compiler.Compile(request, cancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = (WorkspaceOpened)await Open(workspace, "Close race");

        var compilation = workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        await compilationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closed = await workspace.DispatchAsync(
            new CloseWorkspace(opened.WorkspaceId),
            CancellationToken.None);
        releaseCompilation.TrySetResult();
        var compilationOutcome = await compilation;
        var read = await workspace.ReadAsync(opened.WorkspaceId, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(compilationOutcome).IsTypeOf<WorkspaceCommandRejected>();
            await Assert.That(((WorkspaceCommandRejected)compilationOutcome).Code)
                .IsEqualTo("workspace_not_found");
            await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
        }
    }

    private static Task<WorkspaceOpenOutcome> Open(
        IEditorWorkspace workspace,
        string projectDisplayName)
    {
        return workspace.OpenAsync(
            new CreateSandbox(projectDisplayName, "Main"),
            CancellationToken.None);
    }
}
