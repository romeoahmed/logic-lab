using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed partial class EditorWorkspaceTests
{
    [Test]
    public async Task DispatchAsync_ExplicitInitialProbes_PreservesOrderAndIdempotency(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(WorkspaceModuleOperations.Production);
        var (opened, input) = await OpenInputOutputProject(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        var firstOutput = await FindByContract(workspace, opened, "sink.output");
        await Apply(workspace, opened, Place(definitionId, "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(8, 0)));
        var inverter = await FindByContract(workspace, opened, "logic.not");
        await Apply(workspace, opened, Place(definitionId, "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ], new GridPoint(12, 0)));
        var secondOutput = (await Read(workspace, opened)).ProjectRevision.Document
            .EntryCircuitDefinition.ComponentInstances.Single(component =>
                component.Target is LibraryComponentTarget { ContractKey.ContractId: "sink.output" }
                && component.Id != firstOutput.Id);
        await Apply(workspace, opened, new ConnectTerminalsIntent(
            [Terminal(definitionId, input, "Q"), Terminal(definitionId, inverter, "A")]));
        await Apply(workspace, opened, new ConnectTerminalsIntent(
            [Terminal(definitionId, inverter, "Q"), Terminal(definitionId, secondOutput, "D")]));
        var compiled = await CompileSessionProject(workspace, opened, cancellationToken);
        var sources = SessionConfigurationV1.ForEntryOutputs(compiled.ProjectRevision)
            .InitialProbes.Reverse().ToArray();
        var command = new CreateSession(
            Context(opened.WorkspaceId, opened.Attachment, "explicit-session"),
            EditorWorkspaceTestDriver.SessionCreation(compiled),
            SessionConfigurationV1.ForWorkbench(sources));

        var requestedSources = sources.ToArray();
        Array.Reverse(sources);
        var created = (SimulationSessionCreated)await workspace.DispatchAsync(command, cancellationToken);
        var replay = await workspace.DispatchAsync(command, cancellationToken);
        var conflict = (WorkspaceCommandRejected)await workspace.DispatchAsync(new CreateSession(
            command.Context, command.Precondition,
            SessionConfigurationV1.ForWorkbench(sources)), cancellationToken);
        var after = await Read(workspace, opened);

        using (Assert.Multiple())
        {
            await Assert.That(sources).Count().IsEqualTo(2);
            await Assert.That(created.Simulation.Probes.Select(probe => probe.Source))
                .IsEquivalentTo(requestedSources, CollectionOrdering.Matching);
            await Assert.That(created.Simulation).IsEqualTo(after.Simulation);
            await Assert.That(replay).IsEqualTo(created);
            await Assert.That(conflict.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(after.ProjectionVersion).IsEqualTo(compiled.ProjectionVersion + 1);
        }
    }

    [Test]
    public async Task DispatchAsync_EmptyInitialProbes_CreatesSessionWithoutOutputComponents(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(WorkspaceModuleOperations.Production);
        var opened = await Open(workspace, cancellationToken);
        var definitionId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, opened, Place(definitionId, "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ], new GridPoint(0, 0)));
        var compiled = await CompileSessionProject(workspace, opened, cancellationToken);

        var outcome = await workspace.DispatchAsync(new CreateSession(
            Context(opened.WorkspaceId, opened.Attachment, "no-probes"),
            EditorWorkspaceTestDriver.SessionCreation(compiled),
            SessionConfigurationV1.ForWorkbench([])), cancellationToken);

        var created = (await Assert.That(outcome).IsTypeOf<SimulationSessionCreated>())!;
        await Assert.That(created.Simulation.Probes).IsEmpty();
    }

    [Test]
    [Arguments("simulation-id")]
    [Arguments("simulation-revision")]
    [Arguments("trace-id")]
    [Arguments("trace-revision")]
    public async Task DispatchAsync_UnknownSessionPolicy_RejectsBeforeOpeningRuntime(
        string changedField,
        CancellationToken cancellationToken)
    {
        var openCount = 0;
        var production = WorkspaceModuleOperations.Production;
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(production with
        {
            OpenSimulation = (request, token) =>
            {
                Interlocked.Increment(ref openCount);
                return production.OpenSimulation(request, token);
            },
        });
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var before = await CompileSessionProject(workspace, opened, cancellationToken);
        var configuration = new SessionConfigurationV1(
            new SimulationPolicyReference(
                changedField == "simulation-id" ? "unknown" : "workbench-simulation",
                changedField == "simulation-revision" ? "unknown" : "2"),
            new TracePolicyReference(
                changedField == "trace-id" ? "unknown" : "workbench-trace",
                changedField == "trace-revision" ? "unknown" : "1"), []);

        var outcome = (WorkspaceCommandRejected)await workspace.DispatchAsync(new CreateSession(
            Context(opened.WorkspaceId, opened.Attachment, "unknown-policy"),
            EditorWorkspaceTestDriver.SessionCreation(before), configuration), cancellationToken);
        var after = await Read(workspace, opened);

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(openCount).IsEqualTo(0);
            await Assert.That(after.Simulation).IsNull();
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
        }
    }

    [Test]
    public async Task DispatchAsync_DuplicateInitialProbe_RejectsWithoutPublishingSession(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(WorkspaceModuleOperations.Production);
        var (opened, _) = await OpenInputOutputProject(workspace, cancellationToken);
        var before = await CompileSessionProject(workspace, opened, cancellationToken);
        var source = SessionConfigurationV1.ForEntryOutputs(before.ProjectRevision).InitialProbes.Single();

        var outcome = (WorkspaceCommandRejected)await workspace.DispatchAsync(new CreateSession(
            Context(opened.WorkspaceId, opened.Attachment, "duplicate-probe"),
            EditorWorkspaceTestDriver.SessionCreation(before),
            SessionConfigurationV1.ForWorkbench([source, source])), cancellationToken);
        var after = await Read(workspace, opened);

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(after.Simulation).IsNull();
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
        }
    }

    [Test]
    public async Task DispatchAsync_RestartSession_UsesTargetArtifactAndFreshStateAndProbeIds(
        CancellationToken cancellationToken)
    {
        var handles = new List<SimulationSessionHandle>();
        var production = WorkspaceModuleOperations.Production;
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(production with
        {
            OpenSimulation = (request, token) =>
            {
                var outcome = production.OpenSimulation(request, token);
                if (outcome is SimulationOpened opened) handles.Add(opened.Handle);
                return outcome;
            },
        });
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);
        var initial = await Read(workspace, opened);
        await ScheduleSessionInput(workspace, opened, input, 1, cancellationToken);
        var scheduled = await Read(workspace, opened);
        _ = await workspace.DispatchAsync(Step(opened, scheduled), cancellationToken);
        await ScheduleSessionInput(workspace, opened, input, 5, cancellationToken);
        await Apply(workspace, opened, new RenameComponentInstanceIntent(
            initial.ProjectRevision.Document.EntryCircuitDefinitionId, input.Id, "Renamed input"));
        var before = await CompileSessionProject(workspace, opened, cancellationToken);
        var configuration = SessionConfigurationV1.ForWorkbench(
            [.. before.Simulation!.Probes.Select(probe => probe.Source)]);
        var command = new RestartSession(
            Context(opened.WorkspaceId, opened.Attachment, "restart"),
            EditorWorkspaceTestDriver.SessionMutation(before),
            before.PublishedCompilation().ArtifactKey, configuration);

        var restarted = (SimulationSessionRestarted)await workspace.DispatchAsync(command, cancellationToken);
        var replay = await workspace.DispatchAsync(command, cancellationToken);
        var after = await Read(workspace, opened);
        var step = await workspace.DispatchAsync(Step(opened, after), cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(before.Simulation.LogicalTime).IsEqualTo(1UL);
            await Assert.That(before.Simulation.Probes.Single().Value.Single()).IsEqualTo(LogicValue.One);
            await Assert.That(restarted.PreviousSessionId).IsEqualTo(initial.Simulation!.SessionId);
            await Assert.That(restarted.Simulation.SessionId).IsNotEqualTo(initial.Simulation.SessionId);
            await Assert.That(restarted.Simulation.CompilationArtifactKey)
                .IsEqualTo(before.PublishedCompilation().ArtifactKey);
            await Assert.That(restarted.Simulation.Probes.Single().ProbeId)
                .IsNotEqualTo(initial.Simulation.Probes.Single().ProbeId);
            await Assert.That(restarted.Simulation.Probes.Single().Value.Single()).IsEqualTo(LogicValue.Zero);
            await Assert.That(restarted.Simulation.LogicalTime).IsEqualTo(0UL);
            await Assert.That(after.Simulation).IsEqualTo(restarted.Simulation);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion + 1);
            await Assert.That(step).IsTypeOf<LogicLab.Application.Workspaces.NoScheduledStimulus>();
            await Assert.That(replay).IsEqualTo(restarted);
            await Assert.That(handles).Count().IsEqualTo(2);
            await Assert.That(SimulationRuntime.Close(handles[0])).IsTypeOf<SessionAlreadyClosed>();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DispatchAsync_RestartCandidateReadFails_PreservesOldSessionAndClosesCandidate(
        bool cancel,
        CancellationToken cancellationToken)
    {
        var failCandidate = false;
        var closedHandles = new List<SimulationSessionHandle>();
        var production = WorkspaceModuleOperations.Production;
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(production with
        {
            ReadSimulation = (handle, query, token) =>
            {
                var result = production.ReadSimulation(handle, query, token);
                if (failCandidate)
                {
                    if (!cancel) throw new IOException("private candidate failure");
                    requestCancellation.Cancel();
                }
                return result;
            },
            CloseSimulation = handle =>
            {
                closedHandles.Add(handle);
                return production.CloseSimulation(handle);
            },
        });
        var (opened, _) = await OpenInputOutputSession(workspace, cancellationToken);
        var before = await Read(workspace, opened);
        failCandidate = true;

        var outcome = (WorkspaceCommandRejected)await workspace.DispatchAsync(new RestartSession(
            Context(opened.WorkspaceId, opened.Attachment, "failed-restart"),
            EditorWorkspaceTestDriver.SessionMutation(before),
            before.PublishedCompilation().ArtifactKey,
            SessionConfigurationV1.ForEntryOutputs(before.ProjectRevision)), requestCancellation.Token);
        failCandidate = false;
        var after = await Read(workspace, opened);

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Code)
                .IsEqualTo(cancel ? "workspace_cancelled" : "workspace_infrastructure_failure");
            await Assert.That(after.Simulation).IsEqualTo(before.Simulation);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(closedHandles).Count().IsEqualTo(1);
            await Assert.That(SimulationRuntime.Close(closedHandles[0])).IsTypeOf<SessionAlreadyClosed>();
        }
    }

    [Test]
    public async Task DispatchAsync_CloseSession_ReplaysWithoutClosingTheReplacementSession(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(WorkspaceModuleOperations.Production);
        var (opened, _) = await OpenInputOutputSession(workspace, cancellationToken);
        var before = await Read(workspace, opened);
        var command = new CloseSession(
            Context(opened.WorkspaceId, opened.Attachment, "close-session"),
            EditorWorkspaceTestDriver.SessionMutation(before));

        var closed = (SimulationSessionClosed)await workspace.DispatchAsync(command, cancellationToken);
        var afterClose = await Read(workspace, opened);
        var created = (SimulationSessionCreated)await workspace.DispatchAsync(
            Session(opened, afterClose), cancellationToken);
        var replay = await workspace.DispatchAsync(command, cancellationToken);
        var afterReplay = await Read(workspace, opened);

        using (Assert.Multiple())
        {
            await Assert.That(closed.SessionId).IsEqualTo(before.Simulation!.SessionId);
            await Assert.That(afterClose.Simulation).IsNull();
            await Assert.That(afterClose.ProjectRevision).IsEqualTo(before.ProjectRevision);
            await Assert.That(afterClose.Compilation).IsEqualTo(before.Compilation);
            await Assert.That(afterClose.ProjectionVersion).IsEqualTo(before.ProjectionVersion + 1);
            await Assert.That(created.SessionId).IsNotEqualTo(closed.SessionId);
            await Assert.That(replay).IsEqualTo(closed);
            await Assert.That(afterReplay.Simulation).IsEqualTo(created.Simulation);
        }
    }

    [Test]
    [Arguments(false, "version")]
    [Arguments(true, "version")]
    [Arguments(false, "attachment")]
    [Arguments(true, "attachment")]
    [Arguments(true, "artifact")]
    public async Task DispatchAsync_StaleSessionLifecycleCommand_PreservesCurrentSession(
        bool restart,
        string changedFence,
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(WorkspaceModuleOperations.Production);
        var (opened, input) = await OpenInputOutputSession(workspace, cancellationToken);
        var before = await Read(workspace, opened);
        var commandContext = Context(opened.WorkspaceId, opened.Attachment, "stale-session-command");
        WorkspaceCommand command = restart
            ? new RestartSession(commandContext, EditorWorkspaceTestDriver.SessionMutation(before),
                before.PublishedCompilation().ArtifactKey,
                SessionConfigurationV1.ForEntryOutputs(before.ProjectRevision))
            : new CloseSession(commandContext, EditorWorkspaceTestDriver.SessionMutation(before));
        if (changedFence == "version")
        {
            await ScheduleSessionInput(workspace, opened, input, 1, cancellationToken);
        }
        else if (changedFence == "attachment")
        {
            var attached = (Attached)await workspace.AttachAsync(new Reattach(
                opened.WorkspaceId, opened.Attachment.AttachmentId, opened.Attachment.Generation,
                WorkspaceBuild.TestFingerprint, AnonymousWorkspaceCaller.Instance), cancellationToken);
            opened = opened with { Attachment = attached };
        }
        else
        {
            await Apply(workspace, opened, new RenameCircuitDefinitionIntent(
                before.ProjectRevision.Document.EntryCircuitDefinitionId, "New compilation"));
            _ = await CompileSessionProject(workspace, opened, cancellationToken);
        }
        var current = await Read(workspace, opened);

        var rejected = (WorkspaceCommandRejected)await workspace.DispatchAsync(command, cancellationToken);
        var after = await Read(workspace, opened);

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo(changedFence == "attachment"
                ? "stale_workspace_attachment" : "session_precondition_failed");
            await Assert.That(after.Simulation).IsEqualTo(current.Simulation);
            await Assert.That(after.ProjectionVersion).IsEqualTo(current.ProjectionVersion);
        }
    }

    private static async Task<WorkspaceProjection> CompileSessionProject(
        IEditorWorkspace workspace, ControlledWorkspace opened, CancellationToken cancellationToken)
    {
        var before = await Read(workspace, opened);
        _ = await workspace.DispatchAsync(Compilation(opened, before), cancellationToken);
        return await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace, opened.WorkspaceId, opened.Attachment, cancellationToken);
    }

    private static async Task ScheduleSessionInput(
        IEditorWorkspace workspace, ControlledWorkspace opened, ComponentInstance input,
        ulong logicalTime, CancellationToken cancellationToken)
    {
        var before = await Read(workspace, opened);
        var outcome = await workspace.DispatchAsync(EditorWorkspaceTestDriver.ScheduleInput(
            Context(opened.WorkspaceId, opened.Attachment, $"schedule-{logicalTime}"),
            EditorWorkspaceTestDriver.SessionMutation(before), logicalTime,
            input.Id, [LogicValue.One]), cancellationToken);
        var scheduled = (await Assert.That(outcome).IsTypeOf<StimulusScheduled>())!;
        var after = await Read(workspace, opened);
        await Assert.That(scheduled.SessionVersion).IsEqualTo(after.Simulation!.SessionVersion);
    }
}
