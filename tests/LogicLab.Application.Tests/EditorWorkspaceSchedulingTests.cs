using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var controlled = await Open(workspace, "Accepted", cancellationToken);

        var dispatch = workspace.DispatchAsync(
            CompilationCommand(controlled, "compile"),
            cancellationToken);
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            var accepted = (await Assert.That(await dispatch.WaitAsync(cancellationToken))
                .IsTypeOf<CompilationAccepted>())!;
            var running = ((ProjectionSnapshot)await workspace.ReadAsync(
                EditorWorkspaceTestDriver.Query(
                    controlled.Opened.WorkspaceId,
                    controlled.Attached),
                ReadProjection.Instance,
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
        var publishedCompilation = published.PublishedCompilation();
        using (Assert.Multiple())
        {
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(published.Compilation.Generation?.Value).IsEqualTo(1UL);
            await Assert.That(publishedCompilation.ArtifactKey).IsNotNull();
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1),
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
        var rejection = (await Assert.That(rejected)
            .IsTypeOf<WorkspaceCommandRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(rejection.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-scheduling",
                    "1",
                    "compilation_queue_items",
                    2));
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1),
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
        var accepted = (await Assert.That(await workspace.DispatchAsync(
                CompilationCommand(controlled, "accepted"),
                cancellationToken))
            .IsTypeOf<CompilationAccepted>())!;

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
                ReadProjection.Instance,
                cancellationToken)).Projection;
        }
        finally
        {
            compilationGate.Release();
        }

        var rejection = (await Assert.That(rejected)
            .IsTypeOf<WorkspaceCommandRejected>())!;

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
        var publishedCompilation = published.PublishedCompilation();
        var initialCompilation = initial.PublishedCompilation();
        using (Assert.Multiple())
        {
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(published.Compilation.Generation)
                .IsEqualTo(accepted.CompilationGeneration);
            await Assert.That(publishedCompilation.ArtifactKey)
                .IsEqualTo(initialCompilation.ArtifactKey);
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(2, 1),
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
        var firstAcceptance = (await Assert.That(firstOutcome)
            .IsTypeOf<CompilationAccepted>())!;
        var secondAcceptance = (await Assert.That(secondOutcome)
            .IsTypeOf<CompilationAccepted>())!;
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
    public async Task DispatchAsync_RepeatedPendingCompilation_CoalescesWithoutConsumingCapacity(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var invocationCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    compilationGate.Block(CancellationToken.None);
                }

                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1),
            operations: operations);
        var opened = await Open(workspace, "Pending coalescing", cancellationToken);
        var first = await workspace.DispatchAsync(
            CompilationCommand(opened, "first"),
            cancellationToken);
        await Assert.That(first).IsTypeOf<CompilationAccepted>();

        CompilationAccepted? secondAcceptance = null;
        CompilationAccepted? thirdAcceptance = null;
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            secondAcceptance = await Assert.That(await workspace.DispatchAsync(
                    CompilationCommand(opened, "second"),
                    cancellationToken))
                .IsTypeOf<CompilationAccepted>();
            thirdAcceptance = await Assert.That(await workspace.DispatchAsync(
                    CompilationCommand(opened, "third"),
                    cancellationToken))
                .IsTypeOf<CompilationAccepted>();
        }
        finally
        {
            compilationGate.Release();
        }

        Assert.NotNull(secondAcceptance);
        Assert.NotNull(thirdAcceptance);
        var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.Opened.WorkspaceId,
            opened.Attached,
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(thirdAcceptance.CompilationGeneration.Value)
                .IsGreaterThan(secondAcceptance.CompilationGeneration.Value);
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(published.Compilation.Generation)
                .IsEqualTo(thirdAcceptance.CompilationGeneration);
            await Assert.That(invocationCount).IsEqualTo(2);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_CoalescedCompilationThenClose_ReleasesSessionAfterAbandonedGeneration(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var compilationCount = 0;
        var closeSimulationCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref compilationCount) == 2)
                {
                    compilationGate.Block(CancellationToken.None);
                }

                return production.Compile(request, operationCancellationToken);
            },
            CloseSimulation = handle =>
            {
                Interlocked.Increment(ref closeSimulationCount);
                return production.CloseSimulation(handle);
            },
        };
        var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1));

        try
        {
            var opened = await Open(workspace, "Abandoned generation", cancellationToken);
            var authored = await AuthorInputOutput(
                workspace,
                opened,
                cancellationToken);
            RequestCompilation Compilation(string intentId) => new(
                EditorWorkspaceTestDriver.Command(
                    opened.Opened.WorkspaceId,
                    opened.Attached,
                    intentId),
                EditorWorkspaceTestDriver.Compilation(authored));
            _ = await workspace.DispatchAsync(
                Compilation("initial-compilation"),
                cancellationToken);
            var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
                workspace,
                opened.Opened.WorkspaceId,
                opened.Attached,
                cancellationToken);
            var session = await workspace.DispatchAsync(
                new CreateSession(
                    EditorWorkspaceTestDriver.Command(
                        opened.Opened.WorkspaceId,
                        opened.Attached,
                        "create-session"),
                    EditorWorkspaceTestDriver.SessionCreation(published)),
                cancellationToken);
            await Assert.That(session).IsTypeOf<SimulationSessionCreated>();

            _ = await workspace.DispatchAsync(
                Compilation("running-compilation"),
                cancellationToken);
            await compilationGate.Started.WaitAsync(cancellationToken);
            _ = await workspace.DispatchAsync(
                Compilation("abandoned-pending-compilation"),
                cancellationToken);
            _ = await workspace.DispatchAsync(
                Compilation("replacement-pending-compilation"),
                cancellationToken);

            var closed = await workspace.DispatchAsync(
                new CloseWorkspace(EditorWorkspaceTestDriver.Command(
                    opened.Opened.WorkspaceId,
                    opened.Attached,
                    "close")),
                cancellationToken);
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();

            compilationGate.Release();
            await workspace.DisposeAsync();

            await Assert.That(closeSimulationCount).IsEqualTo(1);
        }
        finally
        {
            compilationGate.Release();
            await workspace.DisposeAsync();
        }
    }

    private static async Task<WorkspaceProjection> AuthorInputOutput(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        CancellationToken cancellationToken)
    {
        var projection = controlled.Attached.Projection;
        var definitionId = projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        projection = await Apply(
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey("logiclab.core", "source.input"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ],
                new ComponentPlacement(new GridPoint(0, 0))),
            "place-input");
        projection = await Apply(
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey("logiclab.core", "sink.output"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "radix",
                        new ChoiceParameterValue("binary")),
                ],
                new ComponentPlacement(new GridPoint(4, 0))),
            "place-sink");
        var instances = projection.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances;
        var input = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId
                == "source.input");
        var sink = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId
                == "sink.output");
        return await Apply(
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, input.Id, "Q"),
                    new InstanceTerminalReference(definitionId, sink.Id, "D"),
                ]),
            "connect");

        async Task<WorkspaceProjection> Apply(EditIntent intent, string intentId)
        {
            var outcome = await workspace.DispatchAsync(
                new ApplyEdit(
                    EditorWorkspaceTestDriver.Command(
                        controlled.Opened.WorkspaceId,
                        controlled.Attached,
                        intentId),
                    new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                    intent),
                cancellationToken);
            _ = await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
            var read = await workspace.ReadAsync(
                EditorWorkspaceTestDriver.Query(
                    controlled.Opened.WorkspaceId,
                    controlled.Attached),
                ReadProjection.Instance,
                cancellationToken);
            return ((ProjectionSnapshot)read).Projection;
        }
    }

    [Test, Timeout(30_000)]
    public async Task ReadAsync_SupersededCompilationGeneration_ReportsNewerGeneration(
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(2, 1),
            operations: operations);
        var opened = await Open(workspace, "Observable supersession", cancellationToken);
        var first = await workspace.DispatchAsync(
            CompilationCommand(opened, "first"),
            cancellationToken);
        var firstAcceptance = (await Assert.That(first).IsTypeOf<CompilationAccepted>())!;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            var second = await workspace.DispatchAsync(
                CompilationCommand(opened, "second"),
                cancellationToken);
            var secondAcceptance = (await Assert.That(second)
                .IsTypeOf<CompilationAccepted>())!;

            var read = await workspace.ReadAsync(
                EditorWorkspaceTestDriver.Query(
                    opened.Opened.WorkspaceId,
                    opened.Attached),
                new ReadCompilation(firstAcceptance.CompilationGeneration),
                cancellationToken);
            var snapshot = (await Assert.That(read).IsTypeOf<CompilationSnapshot>())!;
            var superseded = snapshot.Compilation as CompilationSupersededProjection;
            Assert.NotNull(superseded);

            using (Assert.Multiple())
            {
                await Assert.That(snapshot.Compilation.Status)
                    .IsEqualTo(CompilationPublicationStatus.Superseded);
                await Assert.That(snapshot.Compilation.Generation)
                    .IsEqualTo(firstAcceptance.CompilationGeneration);
                await Assert.That(superseded.SupersededBy)
                    .IsEqualTo(secondAcceptance.CompilationGeneration);
            }
        }
        finally
        {
            compilationGate.Release();
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_AcceptedCompilationIgnoresLaterRequestCancellation(
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1),
            operations: operations);
        var blocking = await Open(workspace, "Blocking", cancellationToken);
        var cancelled = await Open(workspace, "Cancelled", cancellationToken);

        _ = await workspace.DispatchAsync(
            CompilationCommand(blocking, "blocking"),
            cancellationToken);
        using var requestCancellation = new CancellationTokenSource();
        CompilationGeneration acceptedGeneration;
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            var outcome = await workspace.DispatchAsync(
                CompilationCommand(cancelled, "cancelled"),
                requestCancellation.Token);
            var accepted = (await Assert.That(outcome).IsTypeOf<CompilationAccepted>())!;
            acceptedGeneration = accepted.CompilationGeneration;
            requestCancellation.Cancel();
        }
        finally
        {
            compilationGate.Release();
        }

        var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            cancelled.Opened.WorkspaceId,
            cancelled.Attached,
            cancellationToken);
        var publishedCompilation = published.PublishedCompilation();
        using (Assert.Multiple())
        {
            await Assert.That(published.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(publishedCompilation.Generation)
                .IsEqualTo(acceptedGeneration);
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
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
            ReadProjection.Instance,
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
            new CreateSandbox(projectDisplayName, "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);

        var opened = (await Assert.That(outcome).IsTypeOf<WorkspaceOpened>())!;
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
