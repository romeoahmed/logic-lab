using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceTests
{
    [Test]
    public async Task DispatchAsync_AttachedWorkspace_ExecutesOperationalCommands()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, input) = await OpenInputOutputProject(workspace);
        var attachment = await Attach(workspace, opened.WorkspaceId);
        var beforeCompilation = await Read(workspace, opened.WorkspaceId);

        var compilationCommand = new RequestCompilation(
            Context(opened.WorkspaceId, attachment, "compile"),
            new CompilationPrecondition(
                beforeCompilation.ProjectRevision.RevisionId,
                beforeCompilation.ProjectRevision.Document.EntryCircuitDefinitionId,
                beforeCompilation.ProjectRevision.Document.LibrarySnapshot.Fingerprint));
        var compilation = await workspace.DispatchAsync(
            compilationCommand,
            CancellationToken.None);
        var published = await Assert.That(compilation).IsTypeOf<CompilationPublished>();
        Assert.NotNull(published);
        var compilationReplay = await workspace.DispatchAsync(
            new RequestCompilation(
                Context(opened.WorkspaceId, attachment, "compile"),
                new CompilationPrecondition(
                    beforeCompilation.ProjectRevision.RevisionId,
                    beforeCompilation.ProjectRevision.Document.EntryCircuitDefinitionId,
                    beforeCompilation.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
            CancellationToken.None);
        var session = await workspace.DispatchAsync(
            new CreateSession(
                Context(opened.WorkspaceId, attachment, "create-session"),
                new SessionCreationPrecondition(published.ArtifactKey)),
            CancellationToken.None);
        var created = await Assert.That(session).IsTypeOf<SimulationSessionCreated>();
        Assert.NotNull(created);
        var afterCreate = await Read(workspace, opened.WorkspaceId);
        var simulation = afterCreate.Simulation!;

        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                Context(opened.WorkspaceId, attachment, "schedule"),
                new SessionMutationPrecondition(
                    simulation.SessionId,
                    simulation.SessionVersion,
                    published.ArtifactKey),
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);
        var afterSchedule = await Read(workspace, opened.WorkspaceId);
        var staleStep = await workspace.DispatchAsync(
            new StepSession(
                Context(opened.WorkspaceId, attachment, "stale-step"),
                new SessionMutationPrecondition(
                    simulation.SessionId,
                    simulation.SessionVersion,
                    published.ArtifactKey)),
            CancellationToken.None);
        var stepped = await workspace.DispatchAsync(
            new StepSession(
                Context(opened.WorkspaceId, attachment, "step"),
                new SessionMutationPrecondition(
                    afterSchedule.Simulation!.SessionId,
                    afterSchedule.Simulation.SessionVersion,
                    published.ArtifactKey)),
            CancellationToken.None);
        var closed = await workspace.DispatchAsync(
            new CloseWorkspace(
                Context(opened.WorkspaceId, attachment, "close")),
            CancellationToken.None);
        var readAfterClose = await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None);

        var readRejection = await Assert.That(readAfterClose)
            .IsTypeOf<WorkspaceReadRejected>();
        Assert.NotNull(readRejection);
        var staleStepRejection = await Assert.That(staleStep)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(staleStepRejection);
        using (Assert.Multiple())
        {
            await Assert.That(compilationReplay).IsSameReferenceAs(published);
            await Assert.That(created.SessionId).IsEqualTo(simulation.SessionId);
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
            await Assert.That(staleStepRejection.Code)
                .IsEqualTo("session_precondition_failed");
            await Assert.That(stepped).IsTypeOf<SessionStepped>();
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(readRejection.Code).IsEqualTo("workspace_not_found");
        }
    }

    [Test]
    public async Task DispatchAsync_UndoWithExistingSession_MovesHistoryAndRetainsSession()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, _) = await OpenInputOutputSession(workspace);
        var before = await Read(workspace, opened.WorkspaceId);
        var attachment = await Attach(workspace, opened.WorkspaceId);

        var outcome = await workspace.DispatchAsync(
            new Undo(
                Context(opened.WorkspaceId, attachment, "undo"),
                new AuthoringPrecondition(before.ProjectRevision.RevisionId)),
            CancellationToken.None);
        var committed = await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
        Assert.NotNull(committed);
        var after = await Read(workspace, opened.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(committed.ProjectRevisionId)
                .IsNotEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.Simulation).IsNotNull();
            await Assert.That(after.Simulation!.SessionId)
                .IsEqualTo(before.Simulation!.SessionId);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(after.History.CanRedo).IsTrue();
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ConcurrentCompilationReplay_ReturnsOneRecordedOutcome(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var compileCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                Interlocked.Increment(ref compileCount);
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var (opened, _) = await OpenInputOutputProject(workspace);
        var attachment = await Attach(workspace, opened.WorkspaceId);
        var projection = await Read(workspace, opened.WorkspaceId);
        var command = new RequestCompilation(
            Context(opened.WorkspaceId, attachment, "compile"),
            new CompilationPrecondition(
                projection.ProjectRevision.RevisionId,
                projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                projection.ProjectRevision.Document.LibrarySnapshot.Fingerprint));

        var first = workspace.DispatchAsync(command, cancellationToken);
        Task<WorkspaceCommandOutcome> replay;
        WorkspaceCommandOutcome? conflictingIntent = null;
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            conflictingIntent = await workspace.DispatchAsync(
                new ApplyEdit(
                    Context(opened.WorkspaceId, attachment, "compile"),
                    new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                    new RenameCircuitDefinitionIntent(
                        projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                        "Must not commit")),
                cancellationToken);
            replay = workspace.DispatchAsync(
                new RequestCompilation(
                    Context(opened.WorkspaceId, attachment, "compile"),
                    new CompilationPrecondition(
                        projection.ProjectRevision.RevisionId,
                        projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                        projection.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        var outcomes = await Task.WhenAll(first, replay).WaitAsync(cancellationToken);
        var conflict = await Assert.That(conflictingIntent!)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(conflict);
        using (Assert.Multiple())
        {
            await Assert.That(outcomes[0]).IsTypeOf<CompilationPublished>();
            await Assert.That(outcomes[1]).IsSameReferenceAs(outcomes[0]);
            await Assert.That(compileCount).IsEqualTo(1);
            await Assert.That(conflict.Code).IsEqualTo("idempotency_key_conflict");
        }
    }

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
        var initialProbe = await Assert.That(initial.Simulation!.Probes).HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(initial.Simulation.LogicalTime).IsEqualTo(0UL);
            await Assert.That(initialProbe.Value).IsEquivalentTo(
                [LogicValue.One]);
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
        var afterStepProbe = await Assert.That(afterStep.Simulation!.Probes).HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(afterStep.Simulation.LogicalTime).IsEqualTo(1UL);
            await Assert.That(afterStepProbe.Value).IsEquivalentTo(
                [LogicValue.Zero]);
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

        var compilationRejection = await Assert.That(compilation)
            .IsTypeOf<WorkspaceCommandRejected>();
        var sessionRejection = await Assert.That(session)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(compilationRejection);
        Assert.NotNull(sessionRejection);
        using (Assert.Multiple())
        {
            await Assert.That(compilationRejection.Code).IsEqualTo("compilation_invalid");
            await Assert.That(sessionRejection.Code).IsEqualTo("session_precondition_failed");
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
        var cancellationToken = new CancellationToken(canceled: true);

        var outcome = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            cancellationToken);
        var after = await Read(workspace, opened.WorkspaceId);

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
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
        var cancellationToken = new CancellationToken(canceled: true);

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                opened.WorkspaceId,
                new AddWireGeometryIntent(
                    definition.Id,
                    net.Id,
                    new UnroutedWireRoute())),
            cancellationToken);
        var after = await Read(workspace, opened.WorkspaceId);

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
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

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        await Assert.That(rejected.Code).IsEqualTo("session_precondition_failed");
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

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        await Assert.That(rejected.Code).IsEqualTo("session_precondition_failed");
    }

    [Test]
    public async Task DispatchAsync_StepWithoutScheduledStimulus_ReturnsSimulationReason()
    {
        await using var workspace = EditorWorkspaceFactory.Create();
        var (opened, _) = await OpenInputOutputSession(workspace);

        var outcome = await workspace.DispatchAsync(
            new StepSession(opened.WorkspaceId),
            CancellationToken.None);

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        await Assert.That(rejected.Code).IsEqualTo("no_scheduled_stimulus");
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
        var (opened, input) = await OpenInputOutputProject(workspace);
        _ = await workspace.DispatchAsync(
            new RequestCompilation(opened.WorkspaceId),
            CancellationToken.None);
        _ = await workspace.DispatchAsync(
            new CreateSession(opened.WorkspaceId),
            CancellationToken.None);
        return (opened, input);
    }

    private static async Task<(WorkspaceOpened Opened, ComponentInstance Input)>
        OpenInputOutputProject(IEditorWorkspace workspace)
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
        return (opened, input);
    }

    private static async Task<Attached> Attach(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        var outcome = await workspace.AttachAsync(
            new InitialAttach(workspaceId, WorkspaceBuild.DevelopmentFingerprint),
            CancellationToken.None);
        var attached = await Assert.That(outcome).IsTypeOf<Attached>();
        return attached!;
    }

    private static WorkspaceCommandContext Context(
        WorkspaceId workspaceId,
        Attached attachment,
        string clientIntentId)
    {
        return new WorkspaceCommandContext(
            workspaceId,
            attachment.AttachmentId,
            attachment.Generation,
            new ClientIntentId(clientIntentId));
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
