using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceFailureTests
{
    [Test]
    [Arguments(false, "workspace_internal_defect")]
    [Arguments(true, "workspace_infrastructure_failure")]
    public async Task DispatchAsync_CompilationDelegateThrows_ReturnsOpaqueFailureWithoutPublication(
        bool infrastructureFailure,
        string expectedCode)
    {
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, _) => throw Failure(infrastructureFailure),
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = (WorkspaceOpened)await Open(workspace);
        var before = opened.Projection;

        var outcome = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        var after = (ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)outcome).Code)
                .IsEqualTo(expectedCode);
            await Assert.That(((WorkspaceCommandRejected)outcome).DiagnosticCodes).IsEmpty();
            await Assert.That(after.Projection.ProjectionVersion)
                .IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Projection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
        }
    }

    [Test]
    [Arguments(false, "workspace_internal_defect")]
    [Arguments(true, "workspace_infrastructure_failure")]
    public async Task DispatchAsync_SessionDelegateThrows_ReturnsOpaqueFailureWithoutPublication(
        bool infrastructureFailure,
        string expectedCode)
    {
        var operations = WorkspaceModuleOperations.Production with
        {
            OpenSimulation = (_, _) => throw Failure(infrastructureFailure),
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace);
        var before = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        var outcome = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)outcome).Code)
                .IsEqualTo(expectedCode);
            await Assert.That(((WorkspaceCommandRejected)outcome).DiagnosticCodes).IsEmpty();
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Compilation).IsEqualTo(before.Compilation);
            await Assert.That(after.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_CreateSessionSnapshotThrows_ClosesOpenedSessionWithoutPublication()
    {
        var closeCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            ReadSimulation = (_, _, _) =>
                throw new IOException("sensitive snapshot detail"),
            CloseSimulation = handle =>
            {
                Interlocked.Increment(ref closeCount);
                return WorkspaceModuleOperations.Production.CloseSimulation(handle);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace);
        var before = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        var outcome = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)outcome).Code)
                .IsEqualTo("workspace_infrastructure_failure");
            await Assert.That(((WorkspaceCommandRejected)outcome).DiagnosticCodes).IsEmpty();
            await Assert.That(closeCount).IsEqualTo(1);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_SessionCommands_PublishFromCommittedOutcomesWithoutExtraSnapshotReads()
    {
        var readCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            ReadSimulation = (handle, query, cancellationToken) =>
            {
                if (Interlocked.Increment(ref readCount) > 1)
                {
                    throw new IOException("unexpected post-command snapshot read");
                }

                return WorkspaceModuleOperations.Production.ReadSimulation(
                    handle,
                    query,
                    cancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace);
        var input = await Find(workspace, opened.WorkspaceId, "source.input");

        var session = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                opened.WorkspaceId,
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);
        var stepped = await workspace.DispatchAsync(
            new StepSession(opened.WorkspaceId),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        await Assert.That(session).IsTypeOf<SimulationSessionCreated>();
        await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
        await Assert.That(stepped).IsTypeOf<SessionStepped>();
        await Assert.That(after.Simulation).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(readCount).IsEqualTo(1);
            await Assert.That(after.Simulation!.SessionVersion).IsEqualTo(3UL);
            await Assert.That(after.Simulation.LogicalTime).IsEqualTo(1UL);
            await Assert.That(after.Simulation.Probes[0].Value)
                .IsEquivalentTo(new[] { LogicValue.One });
        }
    }

    [Test]
    public async Task DispatchAsync_SessionCleanupThrows_StillClosesWorkspace()
    {
        var operations = WorkspaceModuleOperations.Production with
        {
            CloseSimulation = _ => throw new IOException("sensitive cleanup detail"),
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace);
        var session = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);

        var closed = await workspace.DispatchAsync(
            new CloseWorkspace(opened.WorkspaceId),
            CancellationToken.None);
        var read = await workspace.ReadAsync(opened.WorkspaceId, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(session).IsTypeOf<SimulationSessionCreated>();
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
        }
    }

    private static Task<WorkspaceOpenOutcome> Open(IEditorWorkspace workspace)
    {
        return workspace.OpenAsync(
            new CreateSandbox("Test project", "Main"),
            CancellationToken.None);
    }

    private static Exception Failure(bool infrastructureFailure)
    {
        return infrastructureFailure
            ? new IOException("sensitive infrastructure detail")
            : new InvalidOperationException("sensitive implementation detail");
    }

    private static async Task<WorkspaceOpened> OpenCompiledCircuit(IEditorWorkspace workspace)
    {
        var opened = (WorkspaceOpened)await Open(workspace);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened.WorkspaceId, new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, "source.input"),
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new ComponentPlacement(new GridPoint(0, 0))));
        var input = await Find(workspace, opened.WorkspaceId, "source.input");
        await Apply(workspace, opened.WorkspaceId, new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, "sink.output"),
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new ComponentPlacement(new GridPoint(4, 0))));
        var output = await Find(workspace, opened.WorkspaceId, "sink.output");
        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent([
            new InstanceTerminalReference(definitionId, input.Id, "Q"),
            new InstanceTerminalReference(definitionId, output.Id, "D"),
        ]));
        var compiled = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        await Assert.That(compiled).IsTypeOf<CompilationPublished>();
        return opened;
    }

    private static async Task Apply(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        EditIntent intent)
    {
        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(workspaceId, intent),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static async Task<ComponentInstance> Find(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        string contractId)
    {
        var read = (ProjectionSnapshot)await workspace.ReadAsync(
            workspaceId,
            CancellationToken.None);
        return read.Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
    }
}
