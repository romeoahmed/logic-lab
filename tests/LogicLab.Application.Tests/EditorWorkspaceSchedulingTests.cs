using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceSchedulingTests
{
    [Test, Timeout(30_000)]
    public async Task DispatchAsync_CompilationQueueFull_RejectsThroughWorkspaceBoundary(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var invocationCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    compilationGate.Block(operationCancellationToken);
                }

                return Compiler.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(1, 1),
            operations: operations);
        var firstWorkspace = await Open(workspace, "First", cancellationToken);
        var secondWorkspace = await Open(workspace, "Second", cancellationToken);
        var thirdWorkspace = await Open(workspace, "Third", cancellationToken);

        var first = workspace.DispatchAsync(
            new RequestCompilation(firstWorkspace.WorkspaceId),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;
        WorkspaceCommandOutcome rejected;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(
                new RequestCompilation(secondWorkspace.WorkspaceId),
                cancellationToken);
            rejected = await workspace.DispatchAsync(
                new RequestCompilation(thirdWorkspace.WorkspaceId),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        _ = await first.WaitAsync(cancellationToken);
        _ = await second.WaitAsync(cancellationToken);
        var rejection = await Assert.That(rejected)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_NewerCompilation_SupersedesOlderPublication(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var invocationCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    compilationGate.Block(operationCancellationToken);
                }

                return Compiler.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(2, 1),
            operations: operations);
        var opened = await Open(workspace, "Newest wins", cancellationToken);

        var first = workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(
                new RequestCompilation(opened.WorkspaceId),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        var firstOutcome = await first.WaitAsync(cancellationToken);
        var secondOutcome = await second.WaitAsync(cancellationToken);
        var firstRejection = await Assert.That(firstOutcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(firstRejection);

        using (Assert.Multiple())
        {
            await Assert.That(firstRejection.Code)
                .IsEqualTo("workspace_cancelled");
            await Assert.That(secondOutcome).IsTypeOf<CompilationPublished>();
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_CloseDuringCompilation_PreventsLatePublication(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return Compiler.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await Open(workspace, "Close race", cancellationToken);

        var compilation = workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            cancellationToken);
        WorkspaceCommandOutcome closed;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            closed = await workspace.DispatchAsync(
                new CloseWorkspace(opened.WorkspaceId),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        var compilationOutcome = await compilation.WaitAsync(cancellationToken);
        var read = await workspace.ReadAsync(opened.WorkspaceId, cancellationToken);
        var compilationRejection = await Assert.That(compilationOutcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(compilationRejection);

        using (Assert.Multiple())
        {
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(compilationRejection.Code)
                .IsEqualTo("workspace_not_found");
            await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
        }
    }

    private static async Task<WorkspaceOpened> Open(
        IEditorWorkspace workspace,
        string projectDisplayName,
        CancellationToken cancellationToken)
    {
        var outcome = await workspace.OpenAsync(
            new CreateSandbox(projectDisplayName, "Main"),
            cancellationToken);

        var opened = await Assert.That(outcome).IsTypeOf<WorkspaceOpened>();
        Assert.NotNull(opened);
        return opened;
    }
}
