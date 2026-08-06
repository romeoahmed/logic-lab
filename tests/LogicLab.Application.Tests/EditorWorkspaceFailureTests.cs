using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceFailureTests
{
    [Test]
    [Arguments(false, "workspace_internal_defect")]
    [Arguments(true, "workspace_infrastructure_failure")]
    public async Task DispatchAsync_CompilationDelegateThrows_ReturnsOpaqueFailureWithoutPublication(
        bool infrastructureFailure,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, _) => throw Failure(infrastructureFailure),
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenControlled(workspace, cancellationToken);
        var before = opened.Projection;

        var outcome = await workspace.DispatchAsync(
            new RequestCompilation(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.Compilation(before)),
            CancellationToken.None);
        var after = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attached,
            cancellationToken);

        var accepted = await Assert.That(outcome).IsTypeOf<CompilationAccepted>();
        Assert.NotNull(accepted);
        using (Assert.Multiple())
        {
            await Assert.That(after.ProjectionVersion)
                .IsGreaterThan(before.ProjectionVersion);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Rejected);
            await Assert.That(after.Compilation.Generation)
                .IsEqualTo(accepted.CompilationGeneration);
            await Assert.That(after.Compilation.RejectionCode).IsEqualTo(expectedCode);
            await Assert.That(after.Compilation.DiagnosticCodes).IsEmpty();
        }
    }

    [Test]
    [Arguments(false, "workspace_internal_defect")]
    [Arguments(true, "workspace_infrastructure_failure")]
    public async Task DispatchAsync_SessionDelegateThrows_ReturnsOpaqueFailureWithoutPublication(
        bool infrastructureFailure,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        var operations = WorkspaceModuleOperations.Production with
        {
            OpenSimulation = (_, _) => throw Failure(infrastructureFailure),
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace, cancellationToken);
        var before = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var outcome = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionCreation(before)),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo(expectedCode);
            await Assert.That(rejected.DiagnosticCodes).IsEmpty();
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(before.Compilation.Status);
            await Assert.That(after.Compilation.ArtifactKey)
                .IsEqualTo(before.Compilation.ArtifactKey);
            await Assert.That(after.Compilation.DiagnosticCodes)
                .IsEquivalentTo(before.Compilation.DiagnosticCodes);
            await Assert.That(after.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_UnrelatedCancellationException_ReturnsInternalDefectWithoutPublication(
        CancellationToken testCancellationToken)
    {
        using var callerCancellation = new CancellationTokenSource();
        using var unrelatedCancellation = new CancellationTokenSource();
        var operations = WorkspaceModuleOperations.Production with
        {
            OpenSimulation = (_, _) =>
            {
                callerCancellation.Cancel();
                unrelatedCancellation.Cancel();
                throw new OperationCanceledException(unrelatedCancellation.Token);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace, testCancellationToken);
        var before = await Read(workspace, opened);

        var outcome = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionCreation(before)),
            callerCancellation.Token);
        var after = await Read(workspace, opened);

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_internal_defect");
            await Assert.That(rejected.DiagnosticCodes).IsEmpty();
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_CreateSessionSnapshotThrows_ClosesOpenedSessionWithoutPublication(
        CancellationToken cancellationToken)
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
        var opened = await OpenCompiledCircuit(workspace, cancellationToken);
        var before = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var outcome = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionCreation(before)),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_infrastructure_failure");
            await Assert.That(rejected.DiagnosticCodes).IsEmpty();
            await Assert.That(closeCount).IsEqualTo(1);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_CreateSessionCancelledDuringSnapshot_ClosesHandleWithoutPublication(
        CancellationToken testCancellationToken)
    {
        using var cancellation = new CancellationTokenSource();
        var closeCount = 0;
        var operations = WorkspaceModuleOperations.Production with
        {
            ReadSimulation = (handle, query, cancellationToken) =>
            {
                cancellation.Cancel();
                return WorkspaceModuleOperations.Production.ReadSimulation(
                    handle,
                    query,
                    cancellationToken);
            },
            CloseSimulation = handle =>
            {
                Interlocked.Increment(ref closeCount);
                return WorkspaceModuleOperations.Production.CloseSimulation(handle);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace, testCancellationToken);
        var before = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var outcome = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionCreation(before)),
            cancellation.Token);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
            await Assert.That(closeCount).IsEqualTo(1);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_CommittedSessionMutation_WhenSnapshotReadIsUnavailable_StillPublishesOutcomeState(
        CancellationToken cancellationToken)
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
        var opened = await OpenCompiledCircuit(workspace, cancellationToken);
        var input = await Find(workspace, opened, "source.input");

        var beforeSession = await Read(workspace, opened);

        var session = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionCreation(beforeSession)),
            CancellationToken.None);
        var beforeSchedule = await Read(workspace, opened);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionMutation(beforeSchedule),
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);
        var beforeStep = await Read(workspace, opened);
        var stepped = await workspace.DispatchAsync(
            new StepSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionMutation(beforeStep)),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;

        var created = await Assert.That(session).IsTypeOf<SimulationSessionCreated>();
        var stimulus = await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
        var step = await Assert.That(stepped).IsTypeOf<SessionStepped>();
        Assert.NotNull(created);
        Assert.NotNull(stimulus);
        Assert.NotNull(step);
        await Assert.That(after.Simulation).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(created.ProjectionVersion)
                .IsLessThan(stimulus.ProjectionVersion);
            await Assert.That(stimulus.ProjectionVersion)
                .IsLessThan(step.ProjectionVersion);
            await Assert.That(step.ProjectionVersion).IsEqualTo(after.ProjectionVersion);
            await Assert.That(after.Simulation!.SessionVersion).IsEqualTo(3UL);
            await Assert.That(after.Simulation.LogicalTime).IsEqualTo(1UL);
            await Assert.That(after.Simulation.Probes[0].Value)
                .IsEquivalentTo([LogicValue.One]);
        }
    }

    [Test]
    public async Task DispatchAsync_SessionCleanupThrows_StillClosesWorkspace(
        CancellationToken cancellationToken)
    {
        var operations = WorkspaceModuleOperations.Production with
        {
            CloseSimulation = _ => throw new IOException("sensitive cleanup detail"),
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await OpenCompiledCircuit(workspace, cancellationToken);
        var beforeSession = await Read(workspace, opened);
        var session = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.SessionCreation(beforeSession)),
            CancellationToken.None);

        var closed = await workspace.DispatchAsync(
            new CloseWorkspace(EditorWorkspaceTestDriver.Command(
                opened.WorkspaceId,
                opened.Attached)),
            CancellationToken.None);
        var read = await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, opened.Attached),
            ReadProjection.Instance,
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(session).IsTypeOf<SimulationSessionCreated>();
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
        }
    }

    private static Task<WorkspaceOpenOutcome> Open(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        return workspace.OpenAsync(
            new CreateSandbox("Test project", "Main"),
            cancellationToken);
    }

    private static Exception Failure(bool infrastructureFailure)
    {
        return infrastructureFailure
            ? new IOException("sensitive infrastructure detail")
            : new InvalidOperationException("sensitive implementation detail");
    }

    private static async Task<ControlledWorkspace> OpenControlled(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var opened = (WorkspaceOpened)await Open(workspace, cancellationToken);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);
        return new ControlledWorkspace(opened, attached);
    }

    private static async Task<ControlledWorkspace> OpenCompiledCircuit(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var opened = await OpenControlled(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened, new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, "source.input"),
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new ComponentPlacement(new GridPoint(0, 0))));
        var input = await Find(workspace, opened, "source.input");
        await Apply(workspace, opened, new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, "sink.output"),
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new ComponentPlacement(new GridPoint(4, 0))));
        var output = await Find(workspace, opened, "sink.output");
        await Apply(workspace, opened, new ConnectTerminalsIntent([
            new InstanceTerminalReference(definitionId, input.Id, "Q"),
            new InstanceTerminalReference(definitionId, output.Id, "D"),
        ]));
        var beforeCompilation = await Read(workspace, opened);
        var compiled = await workspace.DispatchAsync(
            new RequestCompilation(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, opened.Attached),
                EditorWorkspaceTestDriver.Compilation(beforeCompilation)),
            CancellationToken.None);
        await Assert.That(compiled).IsTypeOf<CompilationAccepted>();
        var projection = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attached,
            cancellationToken);
        await Assert.That(projection.Compilation.Status)
            .IsEqualTo(CompilationPublicationStatus.Published);
        return opened;
    }

    private static async Task Apply(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        EditIntent intent)
    {
        var projection = await Read(workspace, controlled);
        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                EditorWorkspaceTestDriver.Command(
                    controlled.WorkspaceId,
                    controlled.Attached),
                new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                intent),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static async Task<ComponentInstance> Find(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        string contractId)
    {
        var read = (ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                controlled.WorkspaceId,
                controlled.Attached),
            ReadProjection.Instance,
            CancellationToken.None);
        return read.Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled)
    {
        return ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                controlled.WorkspaceId,
                controlled.Attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;
    }

    private sealed record ControlledWorkspace(
        WorkspaceOpened Opened,
        Attached Attached)
    {
        public WorkspaceId WorkspaceId => Opened.WorkspaceId;

        public WorkspaceProjection Projection => Attached.Projection;
    }
}
