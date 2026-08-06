using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceSchedulingTests
{
    [Test, Timeout(30_000)]
    public async Task DispatchAsync_CompilationAcceptance_DoesNotWaitForPublishedGeneration(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var controlled = await Open(workspace, "Accepted", cancellationToken);

        var dispatch = workspace.DispatchAsync(
            CompilationCommand(controlled, "compile"),
            cancellationToken);
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            var accepted = await Assert.That(await dispatch.WaitAsync(
                    TimeSpan.FromSeconds(1),
                    cancellationToken))
                .IsTypeOf<CompilationAccepted>();
            Assert.NotNull(accepted);
            var running = ((ProjectionSnapshot)await workspace.ReadAsync(
                EditorWorkspaceTestDriver.Query(
                    controlled.Opened.WorkspaceId,
                    controlled.Attached),
                cancellationToken)).Projection;

            using (Assert.Multiple())
            {
                await Assert.That(accepted.CompilationGeneration.Value).IsEqualTo(1UL);
                await Assert.That(running.Compilation.Status)
                    .IsEqualTo(CompilationPublicationStatus.Running);
                await Assert.That(running.Compilation.Generation)
                    .IsEqualTo(accepted.CompilationGeneration);
            }
        }
        finally
        {
            compilationGate.Release();
        }

        var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            controlled.Opened.WorkspaceId,
            controlled.Attached,
            cancellationToken);
        using (Assert.Multiple())
        {
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(published.Compilation.Generation?.Value).IsEqualTo(1UL);
            await Assert.That(published.Compilation.ArtifactKey).IsNotNull();
        }
    }

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
            CompilationCommand(firstWorkspace, "first"),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;
        WorkspaceCommandOutcome rejected;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(
                CompilationCommand(secondWorkspace, "second"),
                cancellationToken);
            rejected = await workspace.DispatchAsync(
                CompilationCommand(thirdWorkspace, "third"),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        await Assert.That(await first.WaitAsync(cancellationToken))
            .IsTypeOf<CompilationAccepted>();
        await Assert.That(await second.WaitAsync(cancellationToken))
            .IsTypeOf<CompilationAccepted>();
        var secondPublished = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            secondWorkspace.Opened.WorkspaceId,
            secondWorkspace.Attached,
            cancellationToken);
        var rejection = await Assert.That(rejected)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(secondPublished.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_RejectedCompilationForSameWorkspace_RetainsAcceptedGeneration(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var invocationCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref invocationCount) == 2)
                {
                    compilationGate.Block(operationCancellationToken);
                }

                return Compiler.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(1, 1),
            operations: operations);
        var controlled = await Open(workspace, "Controlled", cancellationToken);
        var queued = await Open(workspace, "Queued", cancellationToken);

        _ = await workspace.DispatchAsync(
            CompilationCommand(controlled, "initial"),
            cancellationToken);
        var initial = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            controlled.Opened.WorkspaceId,
            controlled.Attached,
            cancellationToken);
        await Assert.That(initial.Compilation.Status)
            .IsEqualTo(CompilationPublicationStatus.Published);
        var accepted = await Assert.That(await workspace.DispatchAsync(
                CompilationCommand(controlled, "accepted"),
                cancellationToken))
            .IsTypeOf<CompilationAccepted>();
        Assert.NotNull(accepted);

        WorkspaceCommandOutcome rejected;
        WorkspaceProjection duringRejection;
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            _ = await workspace.DispatchAsync(
                CompilationCommand(queued, "queued"),
                cancellationToken);
            rejected = await workspace.DispatchAsync(
                CompilationCommand(controlled, "rejected"),
                cancellationToken);
            duringRejection = ((ProjectionSnapshot)await workspace.ReadAsync(
                EditorWorkspaceTestDriver.Query(
                    controlled.Opened.WorkspaceId,
                    controlled.Attached),
                cancellationToken)).Projection;
        }
        finally
        {
            compilationGate.Release();
        }

        var rejection = await Assert.That(rejected)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(duringRejection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Running);
            await Assert.That(duringRejection.Compilation.Generation)
                .IsEqualTo(accepted.CompilationGeneration);
        }

        var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            controlled.Opened.WorkspaceId,
            controlled.Attached,
            cancellationToken);
        using (Assert.Multiple())
        {
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(published.Compilation.Generation)
                .IsEqualTo(accepted.CompilationGeneration);
            await Assert.That(published.Compilation.ArtifactKey)
                .IsEqualTo(initial.Compilation.ArtifactKey);
            await Assert.That(invocationCount).IsEqualTo(3);
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
            CompilationCommand(opened, "first"),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(
                CompilationCommand(opened, "second"),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        var firstOutcome = await first.WaitAsync(cancellationToken);
        var secondOutcome = await second.WaitAsync(cancellationToken);
        var firstAcceptance = await Assert.That(firstOutcome)
            .IsTypeOf<CompilationAccepted>();
        var secondAcceptance = await Assert.That(secondOutcome)
            .IsTypeOf<CompilationAccepted>();
        Assert.NotNull(firstAcceptance);
        Assert.NotNull(secondAcceptance);
        var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.Opened.WorkspaceId,
            opened.Attached,
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(firstAcceptance.CompilationGeneration.Value).IsEqualTo(1UL);
            await Assert.That(secondAcceptance.CompilationGeneration.Value).IsEqualTo(2UL);
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(published.Compilation.Generation)
                .IsEqualTo(secondAcceptance.CompilationGeneration);
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_AcceptedCompilationCancelledBeforeExecution_PublishesRejection(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(1, 1),
            operations: operations);
        var blocking = await Open(workspace, "Blocking", cancellationToken);
        var cancelled = await Open(workspace, "Cancelled", cancellationToken);

        _ = await workspace.DispatchAsync(
            CompilationCommand(blocking, "blocking"),
            cancellationToken);
        using var requestCancellation = new CancellationTokenSource();
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            var accepted = await workspace.DispatchAsync(
                CompilationCommand(cancelled, "cancelled"),
                requestCancellation.Token);
            await Assert.That(accepted).IsTypeOf<CompilationAccepted>();
            requestCancellation.Cancel();
        }
        finally
        {
            compilationGate.Release();
        }

        var rejected = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            cancelled.Opened.WorkspaceId,
            cancelled.Attached,
            cancellationToken);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Rejected);
            await Assert.That(rejected.Compilation.RejectionCode)
                .IsEqualTo("workspace_cancelled");
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
            CompilationCommand(opened, "compile"),
            cancellationToken);
        WorkspaceCommandOutcome closed;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            closed = await workspace.DispatchAsync(
                new CloseWorkspace(EditorWorkspaceTestDriver.Command(
                    opened.Opened.WorkspaceId,
                    opened.Attached,
                    "close")),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        var compilationOutcome = await compilation.WaitAsync(cancellationToken);
        var read = await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                opened.Opened.WorkspaceId,
                opened.Attached),
            cancellationToken);
        await Assert.That(compilationOutcome).IsTypeOf<CompilationAccepted>();

        using (Assert.Multiple())
        {
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
        }
    }

    private static async Task<ControlledWorkspace> Open(
        IEditorWorkspace workspace,
        string projectDisplayName,
        CancellationToken cancellationToken)
    {
        var outcome = await workspace.OpenAsync(
            new CreateSandbox(projectDisplayName, "Main"),
            cancellationToken);

        var opened = await Assert.That(outcome).IsTypeOf<WorkspaceOpened>();
        Assert.NotNull(opened);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);
        return new ControlledWorkspace(opened, attached);
    }

    private static RequestCompilation CompilationCommand(
        ControlledWorkspace workspace,
        string intentId)
    {
        return new RequestCompilation(
            EditorWorkspaceTestDriver.Command(
                workspace.Opened.WorkspaceId,
                workspace.Attached,
                intentId),
            EditorWorkspaceTestDriver.Compilation(workspace.Attached.Projection));
    }

    private sealed record ControlledWorkspace(WorkspaceOpened Opened, Attached Attached);
}
