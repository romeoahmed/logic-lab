using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceTests
{
    [Test]
    public async Task DispatchAsync_ValidNarrowCircuit_ObservesProbeAcrossOneStep()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var opened = await Open(workspace);
        var revision = opened.Projection.ProjectRevision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;

        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened.WorkspaceId, "source.input");

        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0)));
        var logicNot = await FindByContract(workspace, opened.WorkspaceId, "logic.not");

        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0)));
        var output = await FindByContract(workspace, opened.WorkspaceId, "sink.output");

        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, logicNot, "A"),
            ]));
        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, logicNot, "Q"),
                Terminal(definitionId, output, "D"),
            ]));

        var compiled = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        var sessionCreated = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var initial = await Read(workspace, opened.WorkspaceId);

        await Assert.That(compiled).IsTypeOf<CompilationPublished>();
        await Assert.That(sessionCreated).IsTypeOf<SimulationSessionCreated>();
        await Assert.That(initial.Simulation).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(initial.Simulation!.LogicalTime).IsEqualTo(0UL);
            await Assert.That(initial.Simulation.Probes).Count().IsEqualTo(1);
            await Assert.That(initial.Simulation.Probes[0].Value).IsEquivalentTo(
                new[] { LogicValue.One });
        }

        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                opened.WorkspaceId,
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);
        var stepped = await workspace.DispatchAsync(
            new StepSession(opened.WorkspaceId),
            CancellationToken.None);
        var afterStep = await Read(workspace, opened.WorkspaceId);

        await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
        await Assert.That(stepped).IsTypeOf<SessionStepped>();
        await Assert.That(afterStep.Simulation).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(afterStep.Simulation!.LogicalTime).IsEqualTo(1UL);
            await Assert.That(afterStep.Simulation.Probes[0].Value).IsEquivalentTo(
                new[] { LogicValue.Zero });
        }
    }

    [Test]
    public async Task DispatchAsync_IncompleteCircuit_DoesNotPublishArtifactOrCreateSession()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var opened = await Open(workspace);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0)));

        var compilation = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        var session = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        var projection = await Read(workspace, opened.WorkspaceId);

        await Assert.That(compilation).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(session).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)compilation).Code)
                .IsEqualTo("compilation_invalid");
            await Assert.That(((WorkspaceCommandRejected)session).Code)
                .IsEqualTo("session_precondition_failed");
            await Assert.That(projection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Rejected);
            await Assert.That(projection.Compilation.ArtifactKey).IsNull();
            await Assert.That(projection.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_CancelledCompilation_DoesNotChangeProjection()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var opened = await Open(workspace);
        var before = await Read(workspace, opened.WorkspaceId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            cancellation.Token);
        var after = await Read(workspace, opened.WorkspaceId);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)outcome).Code)
                .IsEqualTo("workspace_cancelled");
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(after.Compilation.ArtifactKey).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_ExplicitTopologyEdit_PublishesWholeRevision()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var opened = await Open(workspace);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened.WorkspaceId, "source.input");
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(4, 0)));
        var output = await FindByContract(workspace, opened.WorkspaceId, "sink.output");
        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, output, "D"),
            ]));
        var before = await Read(workspace, opened.WorkspaceId);
        var net = before.ProjectRevision.Document.EntryCircuitDefinition.Nets.Single();

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                opened.WorkspaceId,
                new AddJunctionIntent(
                    definitionId,
                    net.Id,
                    new GridPoint(2, 0),
                    [new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(4, 0)])],
                    [],
                    [])),
            CancellationToken.None);
        var after = await Read(workspace, opened.WorkspaceId);

        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
        using (Assert.Multiple())
        {
            await Assert.That(after.ProjectRevision.RevisionId ==
                before.ProjectRevision.RevisionId).IsFalse();
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.Junctions)
                .Count().IsEqualTo(1);
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.WireGeometries)
                .Count().IsEqualTo(1);
            await Assert.That(after.ProjectionVersion)
                .IsEqualTo(checked(before.ProjectionVersion + 1));
        }
    }

    [Test]
    public async Task DispatchAsync_CancelledTopologyEdit_EmitsNoProjectRevision()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, _) = await OpenInputOutputSession(workspace);
        var before = await Read(workspace, opened.WorkspaceId);
        var definition = before.ProjectRevision.Document.EntryCircuitDefinition;
        var net = definition.Nets.Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                opened.WorkspaceId,
                new AddWireGeometryIntent(
                    definition.Id,
                    net.Id,
                    new UnroutedWireRoute())),
            cancellation.Token);
        var after = await Read(workspace, opened.WorkspaceId);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)outcome).Code)
                .IsEqualTo("workspace_cancelled");
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.WireGeometries)
                .IsEmpty();
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_EditDuringQueuedCompilation_DoesNotPublishDifferentRevision(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return LogicLab.Engine.Compilation.Compiler.Compile(
                    request,
                    operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await Open(workspace);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened.WorkspaceId, "source.input");
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(4, 0)));
        var output = await FindByContract(workspace, opened.WorkspaceId, "sink.output");

        var compilation = workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            cancellationToken);
        WorkspaceProjection edited;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, input, "Q"),
                    Terminal(definitionId, output, "D"),
                ]));
            edited = await Read(workspace, opened.WorkspaceId);
        }
        finally
        {
            compilationGate.Release();
        }

        var outcome = await compilation.WaitAsync(cancellationToken);
        var afterCompilation = await Read(workspace, opened.WorkspaceId);
        var rejection = await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo("project_revision_precondition_failed");
            await Assert.That(afterCompilation.ProjectRevision.RevisionId)
                .IsEqualTo(edited.ProjectRevision.RevisionId);
            await Assert.That(afterCompilation.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(afterCompilation.Compilation.ArtifactKey).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_EmptyInputStimulus_ReturnsClosedPreconditionRejection()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, input) = await OpenInputOutputSession(workspace);

        var outcome = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                opened.WorkspaceId,
                1,
                [new InputStimulusAssignment(input.Id, [])]),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(((WorkspaceCommandRejected)outcome).Code)
            .IsEqualTo("session_precondition_failed");
    }

    [Test]
    public async Task DispatchAsync_WrongWidthInputStimulus_ReturnsClosedPreconditionRejection()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, input) = await OpenInputOutputSession(workspace);

        var outcome = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                opened.WorkspaceId,
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.Zero, LogicValue.One])]),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(((WorkspaceCommandRejected)outcome).Code)
            .IsEqualTo("session_precondition_failed");
    }

    [Test]
    public async Task DispatchAsync_StepWithoutScheduledStimulus_ReturnsSimulationReason()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, _) = await OpenInputOutputSession(workspace);

        var outcome = await workspace.DispatchAsync(
            new StepSession(opened.WorkspaceId),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        await Assert.That(((WorkspaceCommandRejected)outcome).Code)
            .IsEqualTo("no_scheduled_stimulus");
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ConcurrentSessionSteps_SerializeInAdmissionOrder(
        CancellationToken cancellationToken)
    {
        var stepGate = new BlockingOperationGate();
        var stepCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary
                    && Interlocked.Increment(ref stepCount) == 1)
                {
                    stepGate.Block(operationCancellationToken);
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var (opened, input) = await OpenInputOutputSession(workspace);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                opened.WorkspaceId,
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            cancellationToken);

        var first = workspace.DispatchAsync(
            new StepSession(opened.WorkspaceId),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;

        try
        {
            await stepGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(
                new StepSession(opened.WorkspaceId),
                cancellationToken);
        }
        finally
        {
            stepGate.Release();
        }

        var outcomes = await Task.WhenAll(first, second).WaitAsync(cancellationToken);
        var projection = await Read(workspace, opened.WorkspaceId);
        var secondRejection = await Assert.That(outcomes[1])
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(secondRejection);

        using (Assert.Multiple())
        {
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
            await Assert.That(outcomes[0]).IsTypeOf<SessionStepped>();
            await Assert.That(secondRejection.Code)
                .IsEqualTo("no_scheduled_stimulus");
            await Assert.That(projection.Simulation!.LogicalTime).IsEqualTo(1UL);
        }
    }

    private static async Task<(WorkspaceOpened Opened, ComponentInstance Input)>
        OpenInputOutputSession(IEditorWorkspace workspace)
    {
        var opened = await Open(workspace);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened.WorkspaceId, "source.input");
        await Apply(workspace, opened.WorkspaceId, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(4, 0)));
        var output = await FindByContract(workspace, opened.WorkspaceId, "sink.output");
        await Apply(workspace, opened.WorkspaceId, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, output, "D"),
            ]));
        _ = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        _ = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        return (opened, input);
    }

    private static async Task<WorkspaceOpened> Open(IEditorWorkspace workspace)
    {
        var outcome = await workspace.OpenAsync(
            new CreateSandbox("Test project", "Main"),
            CancellationToken.None);
        var opened = await Assert.That(outcome).IsTypeOf<WorkspaceOpened>();
        Assert.NotNull(opened);
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

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        var outcome = await workspace.ReadAsync(workspaceId, CancellationToken.None);
        var snapshot = await Assert.That(outcome).IsTypeOf<ProjectionSnapshot>();
        Assert.NotNull(snapshot);
        return snapshot.Projection;
    }

    private static async Task<ComponentInstance> FindByContract(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        string contractId)
    {
        return (await Read(workspace, workspaceId))
            .ProjectRevision
            .Document
            .EntryCircuitDefinition
            .ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
    }

    private static PlaceComponentInstanceIntent Place(
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        return new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
            parameters,
            new ComponentPlacement(origin));
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstance component,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, component.Id, portId);
    }
}
