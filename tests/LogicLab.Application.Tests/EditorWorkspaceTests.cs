using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceTests
{
    [Test]
    public async Task DispatchAsync_CompilationPolicyExhausted_PreservesPolicyEvidence(
        CancellationToken cancellationToken)
    {
        var projectScalePolicy = new ProjectScalePolicy(
            "test-project-scale",
            "1",
            [
                new(ProjectScaleDimension.DefinitionCount, 1),
                new(ProjectScaleDimension.EntityCount, 100),
                new(ProjectScaleDimension.HierarchyDepth, 16),
                new(ProjectScaleDimension.ElaboratedSlotCount, 100),
                new(ProjectScaleDimension.MemoryCellCount, 100),
            ]);
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) => Compiler.Compile(
                new CompilationRequest(
                    request.ProjectRevision,
                    request.EntryCircuitDefinitionId,
                    request.LibrarySnapshot,
                    projectScalePolicy),
                operationCancellationToken),
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations);
        var controlled = await Open(workspace, cancellationToken);
        await Apply(
            workspace,
            controlled,
            new CreateCircuitDefinitionIntent("Second", []));
        var beforeCompilation = await Read(workspace, controlled);

        _ = await workspace.DispatchAsync(
            Compilation(controlled, beforeCompilation),
            CancellationToken.None);
        var afterCompilation = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            controlled.WorkspaceId,
            controlled.Attachment,
            cancellationToken);
        var rejected = afterCompilation.RejectedCompilation();

        await Assert.That(rejected.PolicyEvidence)
            .IsEqualTo(new PolicyEvidenceProjection(
                "test-project-scale",
                "1",
                "definition_count",
                2));
    }

    [Test]
    public async Task DispatchAsync_EditWithExistingSession_PublishesFreshCompilationAndRetainsSession(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);
        var before = await Read(workspace, opened);

        var edit = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, opened.Attachment, "rename-with-session"),
                new AuthoringPrecondition(before.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Renamed while simulating")),
            CancellationToken.None);
        var after = await Read(workspace, opened);
        var compilation = await workspace.DispatchAsync(
            new RequestCompilation(
                Context(opened.WorkspaceId, opened.Attachment, "compile-after-edit"),
                new CompilationPrecondition(
                    after.ProjectRevision.RevisionId,
                    after.ProjectRevision.Document.EntryCircuitDefinitionId,
                    after.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
            CancellationToken.None);
        var accepted = (await Assert.That(compilation).IsTypeOf<CompilationAccepted>())!;
        var compiled = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attachment,
            cancellationToken);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                Context(opened.WorkspaceId, opened.Attachment, "schedule-after-edit"),
                new SessionMutationPrecondition(
                    compiled.Simulation!.SessionId,
                    compiled.Simulation.SessionVersion,
                    compiled.Simulation.CompilationArtifactKey),
                logicalTime: 1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(edit).IsTypeOf<AuthoringCommitted>();
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Renamed while simulating");
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(compiled.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(compiled.PublishedCompilation().ArtifactKey.ProjectRevisionId)
                .IsEqualTo(after.ProjectRevision.RevisionId);
            await Assert.That(compiled.Simulation.SessionId)
                .IsEqualTo(before.Simulation!.SessionId);
            await Assert.That(compiled.Simulation.CompilationArtifactKey)
                .IsEqualTo(before.Simulation.CompilationArtifactKey);
            await Assert.That(compiled.Simulation.CompilationArtifactKey)
                .IsNotEqualTo(compiled.PublishedCompilation().ArtifactKey);
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
        }
    }

    [Test]
    public async Task DispatchAsync_UndoWithExistingSession_RetainsUsableSession(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);
        var before = await Read(workspace, opened);
        var attachment = opened.Attachment;

        AuthoringCommitted? committed = null;
        for (var index = 0; index < 3; index++)
        {
            var current = await Read(workspace, opened);
            var outcome = await workspace.DispatchAsync(
                new Undo(
                    Context(opened.WorkspaceId, attachment, $"undo-{index}"),
                    new AuthoringPrecondition(current.ProjectRevision.RevisionId)),
                CancellationToken.None);
            committed = await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
        }

        Assert.NotNull(committed);
        var after = await Read(workspace, opened);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                Context(opened.WorkspaceId, attachment, "schedule-after-undo"),
                new SessionMutationPrecondition(
                    after.Simulation!.SessionId,
                    after.Simulation.SessionVersion,
                    after.Simulation.CompilationArtifactKey),
                logicalTime: 1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(committed.ProjectRevisionId)
                .IsNotEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.Simulation).IsNotNull();
            await Assert.That(after.Simulation!.SessionId)
                .IsEqualTo(before.Simulation!.SessionId);
            await Assert.That(after.Simulation.CompilationArtifactKey)
                .IsEqualTo(before.PublishedCompilation().ArtifactKey);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition
                    .ComponentInstances)
                .IsEmpty();
            await Assert.That(after.History.CanRedo).IsTrue();
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var attachment = opened.Attachment;
        var projection = await Read(workspace, opened);
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
        var published = (await Assert.That(outcomes[0]).IsTypeOf<CompilationAccepted>())!;
        var replayed = (await Assert.That(outcomes[1]).IsTypeOf<CompilationAccepted>())!;
        var conflict = (await Assert.That(conflictingIntent!)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(replayed.CompilationGeneration)
                .IsEqualTo(published.CompilationGeneration);
            await Assert.That(replayed.ProjectionVersion)
                .IsEqualTo(published.ProjectionVersion);
            await Assert.That(compileCount).IsEqualTo(1);
            await Assert.That(conflict.Code).IsEqualTo("idempotency_key_conflict");
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_CancelledCompilationReplay_ReturnsRecordedAcceptance(
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var projection = await Read(workspace, opened);
        var command = Compilation(opened.WorkspaceId, opened.Attachment, projection);
        var original = workspace.DispatchAsync(command, CancellationToken.None);

        WorkspaceCommandOutcome replay;
        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            using var replayCancellation = new CancellationTokenSource();
            var pendingReplay = workspace.DispatchAsync(command, replayCancellation.Token);
            replayCancellation.Cancel();
            replay = await pendingReplay.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        _ = await original.WaitAsync(cancellationToken);
        var originalAcceptance = (await Assert.That(await original)
            .IsTypeOf<CompilationAccepted>())!;
        var replayAcceptance = (await Assert.That(replay)
            .IsTypeOf<CompilationAccepted>())!;
        await Assert.That(replayAcceptance.CompilationGeneration)
            .IsEqualTo(originalAcceptance.CompilationGeneration);
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_PriorGenerationCompilation_DoesNotCompleteNewIntent(
        CancellationToken cancellationToken)
    {
        var firstCompilationGate = new BlockingOperationGate();
        var compileCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref compileCount) == 1)
                {
                    firstCompilationGate.Block(CancellationToken.None);
                }

                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var firstAttachment = opened.Attachment;
        var projection = await Read(workspace, opened);
        var first = workspace.DispatchAsync(
            Compilation(opened.WorkspaceId, firstAttachment, projection),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;
        Task<WorkspaceCommandOutcome> replay;
        Attached? secondAttachment = null;

        try
        {
            await firstCompilationGate.Started.WaitAsync(cancellationToken);
            secondAttachment = await Assert.That(await workspace.AttachAsync(
                    new Reattach(
                        opened.WorkspaceId,
                        firstAttachment.AttachmentId,
                        firstAttachment.Generation,
                        WorkspaceBuild.DevelopmentFingerprint,
                        AnonymousWorkspaceCaller.Instance),
                    cancellationToken))
                .IsTypeOf<Attached>();
            Assert.NotNull(secondAttachment);
            second = workspace.DispatchAsync(
                Compilation(opened.WorkspaceId, secondAttachment, projection),
                cancellationToken);
            replay = workspace.DispatchAsync(
                Compilation(opened.WorkspaceId, secondAttachment, projection),
                cancellationToken);
        }
        finally
        {
            firstCompilationGate.Release();
        }

        _ = await first.WaitAsync(cancellationToken);
        var outcomes = await Task.WhenAll(second, replay).WaitAsync(cancellationToken);
        var published = (await Assert.That(outcomes[0]).IsTypeOf<CompilationAccepted>())!;
        var replayed = (await Assert.That(outcomes[1]).IsTypeOf<CompilationAccepted>())!;
        Assert.NotNull(secondAttachment);
        var finalCompilation = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            secondAttachment!,
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(finalCompilation.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(replayed.CompilationGeneration)
                .IsEqualTo(published.CompilationGeneration);
            await Assert.That(replayed.ProjectionVersion)
                .IsEqualTo(published.ProjectionVersion);
            await Assert.That(compileCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task DispatchAsync_ValidNarrowCircuit_ObservesProbeAcrossOneStep(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var opened = await Open(workspace, cancellationToken);
        var revision = opened.Projection.ProjectRevision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;

        await Apply(workspace, opened, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened, "source.input");

        await Apply(workspace, opened, Place(
            definitionId,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0)));
        var logicNot = await FindByContract(workspace, opened, "logic.not");

        await Apply(workspace, opened, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0)));
        var output = await FindByContract(workspace, opened, "sink.output");

        await Apply(workspace, opened, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, logicNot, "A"),
            ]));
        await Apply(workspace, opened, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, logicNot, "Q"),
                Terminal(definitionId, output, "D"),
            ]));

        var beforeCompilation = await Read(workspace, opened);
        var compiled = await workspace.DispatchAsync(
            Compilation(opened, beforeCompilation),
            CancellationToken.None);
        var afterCompilation = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attachment,
            cancellationToken);
        var sessionCreated = await workspace.DispatchAsync(
            Session(opened, afterCompilation),
            CancellationToken.None);
        var initial = await Read(workspace, opened);

        await Assert.That(compiled).IsTypeOf<CompilationAccepted>();
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
                Context(opened.WorkspaceId, opened.Attachment, "schedule"),
                EditorWorkspaceTestDriver.SessionMutation(initial),
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            CancellationToken.None);
        var afterSchedule = await Read(workspace, opened);
        var stepped = await workspace.DispatchAsync(
            Step(opened, afterSchedule),
            CancellationToken.None);
        var afterStep = await Read(workspace, opened);

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
    public async Task DispatchAsync_ReplaceProbes_RemovesBindingAndProjectsEmptySet(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, _) = await OpenInputOutputSession(workspace, cancellationToken);
        var before = await Read(workspace, opened);
        var simulation = before.Simulation!;
        _ = simulation.Probes.Single();

        var outcome = await workspace.DispatchAsync(
            new ReplaceProbes(
                Context(opened.WorkspaceId, opened.Attachment, "replace-probes"),
                EditorWorkspaceTestDriver.SessionMutation(before),
                []),
            CancellationToken.None);
        var after = await Read(workspace, opened);

        var replaced = (await Assert.That(outcome).IsTypeOf<ProbesReplaced>())!;
        using (Assert.Multiple())
        {
            await Assert.That(after.Simulation!.SessionVersion).IsEqualTo(2UL);
            await Assert.That(after.Simulation.Probes).IsEmpty();
            await Assert.That(replaced.ProbeIds).IsEmpty();
        }
    }

    [Test]
    public async Task DispatchAsync_IncompleteCircuit_DoesNotPublishArtifactOrCreateSession(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var opened = await Open(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened, Place(
            definitionId,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0)));

        var beforeCompilation = await Read(workspace, opened);
        var compilation = await workspace.DispatchAsync(
            Compilation(opened, beforeCompilation),
            CancellationToken.None);
        var afterCompilation = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attachment,
            cancellationToken);
        var rejectedCompilation = afterCompilation.RejectedCompilation();
        var session = await workspace.DispatchAsync(
            new CreateSession(
                Context(opened.WorkspaceId, opened.Attachment, "session"),
                new SessionCreationPrecondition(
                    new CompilationArtifactKey(
                        beforeCompilation.ProjectRevision.RevisionId,
                        beforeCompilation.ProjectRevision.Document
                            .EntryCircuitDefinitionId,
                        beforeCompilation.ProjectRevision.Document.LibrarySnapshot.Fingerprint,
                        "missing"))),
            CancellationToken.None);
        var projection = await Read(workspace, opened);

        var compilationAcceptance = (await Assert.That(compilation)
            .IsTypeOf<CompilationAccepted>())!;
        var sessionRejection = (await Assert.That(session)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejectedCompilation.RejectionCode)
                .IsEqualTo("compilation_invalid");
            await Assert.That(rejectedCompilation.Diagnostics).IsNotEmpty();
            await Assert.That(rejectedCompilation.Diagnostics.All(diagnostic =>
                    diagnostic.Source?.Identity is InstancePortSourceIdentity))
                .IsTrue();
            await Assert.That(sessionRejection.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(projection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Rejected);
            await Assert.That(projection.Simulation).IsNull();
        }
    }

    [Test]
    public async Task DispatchAsync_CancelledCompilation_DoesNotChangeProjection(
        CancellationToken testCancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var opened = await Open(workspace, testCancellationToken);
        var before = await Read(workspace, opened);
        var cancellationToken = new CancellationToken(canceled: true);

        var outcome = await workspace.DispatchAsync(
            Compilation(opened, before),
            cancellationToken);
        var after = await Read(workspace, opened);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
        }
    }

    [Test]
    public async Task DispatchAsync_ExplicitTopologyEdit_PublishesWholeRevision(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var opened = await Open(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened, "source.input");
        await Apply(workspace, opened, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(4, 0)));
        var output = await FindByContract(workspace, opened, "sink.output");
        await Apply(workspace, opened, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, output, "D"),
            ]));
        var before = await Read(workspace, opened);
        var net = before.ProjectRevision.Document.EntryCircuitDefinition.Nets.Single();

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, opened.Attachment, "junction"),
                new AuthoringPrecondition(before.ProjectRevision.RevisionId),
                new AddJunctionIntent(
                    definitionId,
                    net.Id,
                    new GridPoint(2, 0),
                    [new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(4, 0)])],
                    [],
                    [])),
            CancellationToken.None);
        var after = await Read(workspace, opened);

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
    public async Task DispatchAsync_CancelledTopologyEdit_EmitsNoProjectRevision(
        CancellationToken testCancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, _) = await OpenInputOutputSession(workspace, testCancellationToken);
        var before = await Read(workspace, opened);
        var definition = before.ProjectRevision.Document.EntryCircuitDefinition;
        var net = definition.Nets.Single();
        var cancellationToken = new CancellationToken(canceled: true);

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, opened.Attachment, "wire"),
                new AuthoringPrecondition(before.ProjectRevision.RevisionId),
                new AddWireGeometryIntent(
                    definition.Id,
                    net.Id,
                    new UnroutedWireRoute())),
            cancellationToken);
        var after = await Read(workspace, opened);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var opened = await Open(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened, "source.input");
        await Apply(workspace, opened, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(4, 0)));
        var output = await FindByContract(workspace, opened, "sink.output");

        var beforeCompilation = await Read(workspace, opened);
        var compilation = workspace.DispatchAsync(
            Compilation(opened, beforeCompilation),
            cancellationToken);
        WorkspaceProjection edited;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            await Apply(workspace, opened, new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, input, "Q"),
                    Terminal(definitionId, output, "D"),
                ]));
            edited = await Read(workspace, opened);
        }
        finally
        {
            compilationGate.Release();
        }

        var outcome = await compilation.WaitAsync(cancellationToken);
        var afterCompilation = await Read(workspace, opened);
        var acceptance = (await Assert.That(outcome)
            .IsTypeOf<CompilationAccepted>())!;

        using (Assert.Multiple())
        {
            await Assert.That(afterCompilation.ProjectRevision.RevisionId)
                .IsEqualTo(edited.ProjectRevision.RevisionId);
            await Assert.That(afterCompilation.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
        }
    }

    [Test]
    public async Task DispatchAsync_EmptyInputStimulus_ReturnsClosedPreconditionRejection(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);

        var outcome = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                Context(opened.WorkspaceId, opened.Attachment, "empty"),
                EditorWorkspaceTestDriver.SessionMutation(
                    await Read(workspace, opened)),
                1,
                [new InputStimulusAssignment(input.Id, [])]),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        await Assert.That(rejected.Code).IsEqualTo("session_precondition_failed");
    }

    [Test]
    public async Task DispatchAsync_WrongWidthInputStimulus_ReturnsClosedPreconditionRejection(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);

        var outcome = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                Context(opened.WorkspaceId, opened.Attachment, "width"),
                EditorWorkspaceTestDriver.SessionMutation(
                    await Read(workspace, opened)),
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.Zero, LogicValue.One])]),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        await Assert.That(rejected.Code).IsEqualTo("session_precondition_failed");
    }

    [Test]
    public async Task DispatchAsync_StepWithoutScheduledStimulus_ReturnsSimulationReason(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var (opened, _) = await OpenInputOutputSession(workspace, cancellationToken);

        var outcome = await workspace.DispatchAsync(
            Step(opened, await Read(workspace, opened)),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        await Assert.That(rejected.Code).IsEqualTo("no_scheduled_stimulus");
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ConcurrentSessionSteps_RejectsStaleSecondPrecondition(
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
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations: operations);
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                Context(opened.WorkspaceId, opened.Attachment, "schedule"),
                EditorWorkspaceTestDriver.SessionMutation(
                    await Read(workspace, opened)),
                1,
                [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
            cancellationToken);

        var stepPrecondition = EditorWorkspaceTestDriver.SessionMutation(
            await Read(workspace, opened));
        var first = workspace.DispatchAsync(
            new StepSession(
                Context(opened.WorkspaceId, opened.Attachment, "first-step"),
                stepPrecondition),
            cancellationToken);
        Task<WorkspaceCommandOutcome> second;

        try
        {
            await stepGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(
                new StepSession(
                    Context(opened.WorkspaceId, opened.Attachment, "second-step"),
                    stepPrecondition),
                cancellationToken);
        }
        finally
        {
            stepGate.Release();
        }

        var outcomes = await Task.WhenAll(first, second).WaitAsync(cancellationToken);
        var projection = await Read(workspace, opened);
        var secondRejection = (await Assert.That(outcomes[1])
            .IsTypeOf<WorkspaceCommandRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
            await Assert.That(outcomes[0]).IsTypeOf<SessionStepped>();
            await Assert.That(secondRejection.Code)
                .IsEqualTo("session_precondition_failed");
            await Assert.That(projection.Simulation!.LogicalTime).IsEqualTo(1UL);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_SessionQueueRejection_RecordsClientIntent(
        CancellationToken cancellationToken)
    {
        var sessionGate = new BlockingOperationGate();
        var openCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            OpenSimulation = (request, operationCancellationToken) =>
            {
                if (Interlocked.Increment(ref openCount) == 1)
                {
                    sessionGate.Block(operationCancellationToken);
                }

                return production.OpenSimulation(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1));
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var attachment = opened.Attachment;
        var beforeCompilation = await Read(workspace, opened);
        var compilation = await workspace.DispatchAsync(
            new RequestCompilation(
                Context(opened.WorkspaceId, attachment, "compile"),
                new CompilationPrecondition(
                    beforeCompilation.ProjectRevision.RevisionId,
                    beforeCompilation.ProjectRevision.Document.EntryCircuitDefinitionId,
                    beforeCompilation.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
            cancellationToken);
        await Assert.That(compilation).IsTypeOf<CompilationAccepted>();
        var compiled = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            attachment,
            cancellationToken);
        var artifactKey = compiled.PublishedCompilation().ArtifactKey;
        var firstCommand = new CreateSession(
            Context(opened.WorkspaceId, attachment, "first"),
            new SessionCreationPrecondition(artifactKey));
        var secondCommand = new CreateSession(
            Context(opened.WorkspaceId, attachment, "second"),
            new SessionCreationPrecondition(artifactKey));
        var rejectedCommand = new CreateSession(
            Context(opened.WorkspaceId, attachment, "rejected"),
            new SessionCreationPrecondition(artifactKey));

        var first = workspace.DispatchAsync(firstCommand, cancellationToken);
        Task<WorkspaceCommandOutcome> second;
        WorkspaceCommandOutcome rejected;
        WorkspaceCommandOutcome stale;
        try
        {
            await sessionGate.Started.WaitAsync(cancellationToken);
            second = workspace.DispatchAsync(secondCommand, cancellationToken);
            rejected = await workspace.DispatchAsync(
                rejectedCommand,
                cancellationToken);
            stale = await workspace.DispatchAsync(
                new CreateSession(
                    new WorkspaceCommandContext(
                        opened.WorkspaceId,
                        new WorkspaceAttachmentId("stale-attachment"),
                        attachment.Generation,
                        new ClientIntentId("stale"),
                        AnonymousWorkspaceCaller.Instance),
                    new SessionCreationPrecondition(artifactKey)),
                cancellationToken);
        }
        finally
        {
            sessionGate.Release();
        }

        _ = await first.WaitAsync(cancellationToken);
        _ = await second.WaitAsync(cancellationToken);
        var replay = await workspace.DispatchAsync(
            rejectedCommand,
            cancellationToken);
        var conflict = await workspace.DispatchAsync(
            new CloseWorkspace(
                Context(opened.WorkspaceId, attachment, "rejected")),
            cancellationToken);
        var after = await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, attachment),
            ReadProjection.Instance,
            cancellationToken);
        var rejection = (await Assert.That(rejected)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var replayRejection = (await Assert.That(replay)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var conflictRejection = (await Assert.That(conflict)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var staleRejection = (await Assert.That(stale)
            .IsTypeOf<WorkspaceCommandRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(replayRejection.Code).IsEqualTo(rejection.Code);
            await Assert.That(replayRejection.DiagnosticCodes)
                .IsEquivalentTo(rejection.DiagnosticCodes, CollectionOrdering.Matching);
            await Assert.That(conflictRejection.Code)
                .IsEqualTo("idempotency_key_conflict");
            await Assert.That(staleRejection.Code)
                .IsEqualTo("stale_workspace_attachment");
            await Assert.That(after).IsTypeOf<ProjectionSnapshot>();
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_CancelledSessionReplay_StopsWaitingForOriginalIntent(
        CancellationToken cancellationToken)
    {
        var sessionGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            OpenSimulation = (request, operationCancellationToken) =>
            {
                sessionGate.Block(operationCancellationToken);
                return production.OpenSimulation(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var beforeCompilation = await Read(workspace, opened);
        var compilation = await workspace.DispatchAsync(
            Compilation(opened.WorkspaceId, opened.Attachment, beforeCompilation),
            cancellationToken);
        await Assert.That(compilation).IsTypeOf<CompilationAccepted>();
        var compiled = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attachment,
            cancellationToken);
        var command = new CreateSession(
            Context(opened.WorkspaceId, opened.Attachment, "session"),
            new SessionCreationPrecondition(compiled.PublishedCompilation().ArtifactKey));
        var original = workspace.DispatchAsync(command, CancellationToken.None);

        WorkspaceCommandOutcome replay;
        try
        {
            await sessionGate.Started.WaitAsync(cancellationToken);
            using var replayCancellation = new CancellationTokenSource();
            var pendingReplay = workspace.DispatchAsync(command, replayCancellation.Token);
            replayCancellation.Cancel();
            replay = await pendingReplay.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        finally
        {
            sessionGate.Release();
        }

        _ = await original.WaitAsync(cancellationToken);
        var rejection = (await Assert.That(replay).IsTypeOf<WorkspaceCommandRejected>())!;
        await Assert.That(rejection.Code).IsEqualTo("workspace_cancelled");
    }

    private static async Task<(ControlledWorkspace Opened, ComponentInstance Input)>
        OpenInputOutputSession(
            IEditorWorkspace workspace,
            CancellationToken cancellationToken)
    {
        var (opened, input) = await OpenInputOutputProject(workspace, cancellationToken);
        var beforeCompilation = await Read(workspace, opened);
        var accepted = await workspace.DispatchAsync(
            Compilation(opened, beforeCompilation),
            cancellationToken);
        var afterCompilation = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            opened.WorkspaceId,
            opened.Attachment,
            cancellationToken);
        var created = await workspace.DispatchAsync(
            Session(opened, afterCompilation),
            cancellationToken);
        await Assert.That(accepted).IsTypeOf<CompilationAccepted>();
        await Assert.That(afterCompilation.Compilation.Status)
            .IsEqualTo(CompilationPublicationStatus.Published);
        await Assert.That(created).IsTypeOf<SimulationSessionCreated>();
        return (opened, input);
    }

    private static async Task<(ControlledWorkspace Opened, ComponentInstance Input)>
        OpenInputOutputProject(
            IEditorWorkspace workspace,
            CancellationToken cancellationToken)
    {
        var opened = await Open(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened, Place(
            definitionId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        var input = await FindByContract(workspace, opened, "source.input");
        await Apply(workspace, opened, Place(
            definitionId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(4, 0)));
        var output = await FindByContract(workspace, opened, "sink.output");
        await Apply(workspace, opened, new ConnectTerminalsIntent(
            [
                Terminal(definitionId, input, "Q"),
                Terminal(definitionId, output, "D"),
            ]));
        return (opened, input);
    }

    private static async Task<Attached> Attach(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        var outcome = await workspace.AttachAsync(
            new InitialAttach(
                workspaceId,
                WorkspaceBuild.DevelopmentFingerprint,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);
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
            new ClientIntentId(clientIntentId),
            AnonymousWorkspaceCaller.Instance);
    }

    private static RequestCompilation Compilation(
        WorkspaceId workspaceId,
        Attached attachment,
        WorkspaceProjection projection)
    {
        return new RequestCompilation(
            Context(workspaceId, attachment, "compile"),
            new CompilationPrecondition(
                projection.ProjectRevision.RevisionId,
                projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                projection.ProjectRevision.Document.LibrarySnapshot.Fingerprint));
    }

    private static RequestCompilation Compilation(
        ControlledWorkspace controlled,
        WorkspaceProjection projection)
    {
        return new RequestCompilation(
            Context(
                controlled.WorkspaceId,
                controlled.Attachment,
                Guid.CreateVersion7().ToString("N")),
            EditorWorkspaceTestDriver.Compilation(projection));
    }

    private static CreateSession Session(
        ControlledWorkspace controlled,
        WorkspaceProjection projection)
    {
        return new CreateSession(
            Context(
                controlled.WorkspaceId,
                controlled.Attachment,
                Guid.CreateVersion7().ToString("N")),
            EditorWorkspaceTestDriver.SessionCreation(projection));
    }

    private static StepSession Step(
        ControlledWorkspace controlled,
        WorkspaceProjection projection)
    {
        return new StepSession(
            Context(
                controlled.WorkspaceId,
                controlled.Attachment,
                Guid.CreateVersion7().ToString("N")),
            EditorWorkspaceTestDriver.SessionMutation(projection));
    }

    private static async Task<ControlledWorkspace> Open(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var outcome = await workspace.OpenAsync(
            new CreateSandbox("Test project", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var opened = (await Assert.That(outcome).IsTypeOf<WorkspaceOpened>())!;
        var attached = await Attach(workspace, opened.WorkspaceId, cancellationToken);
        return new ControlledWorkspace(opened, attached);
    }

    private static async Task Apply(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        EditIntent intent)
    {
        var projection = await Read(workspace, controlled);
        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(controlled.WorkspaceId, controlled.Attachment, Guid.CreateVersion7()
                    .ToString("N")),
                new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                intent),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled)
    {
        var outcome = await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                controlled.WorkspaceId,
                controlled.Attachment),
            ReadProjection.Instance,
            CancellationToken.None);
        var snapshot = (await Assert.That(outcome).IsTypeOf<ProjectionSnapshot>())!;
        return snapshot.Projection;
    }

    private static async Task<ComponentInstance> FindByContract(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        string contractId)
    {
        return (await Read(workspace, controlled))
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

    private sealed record ControlledWorkspace(
        WorkspaceOpened Opened,
        Attached Attachment)
    {
        public WorkspaceId WorkspaceId => Opened.WorkspaceId;

        public WorkspaceProjection Projection => Attachment.Projection;
    }
}
