using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;
using StimulusAssignment = LogicLab.Engine.Simulation.StimulusAssignment;
using StimulusBatch = LogicLab.Engine.Simulation.StimulusBatch;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceHierarchyTests
{
    [Test]
    public async Task DispatchAsync_HierarchicalCircuit_CompilesAndSimulatesAcrossBoundary(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Hierarchy project", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var workspaceId = opened.WorkspaceId;
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            workspaceId,
            cancellationToken);
        var mainId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;

        await Apply(workspace, workspaceId, attached, new CreateCircuitDefinitionIntent(
            "Inverter",
            [
                new DefinitionPortDeclaration(
                    "A",
                    PortDirection.Input,
                    1,
                    new DefinitionPortPlacement(
                        new GridPoint(0, 2),
                        CardinalDirection.West)),
                new DefinitionPortDeclaration(
                    "Q",
                    PortDirection.Output,
                    1,
                    new DefinitionPortPlacement(
                        new GridPoint(8, 2),
                        CardinalDirection.East)),
            ]));
        var child = (await Read(workspace, workspaceId, attached)).ProjectRevision.Document
            .CircuitDefinitions.Single(definition => definition.DisplayName == "Inverter");
        await Apply(workspace, workspaceId, attached, PlaceLibrary(
            child.Id,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 2)));
        child = (await Read(workspace, workspaceId, attached)).ProjectRevision.Document
            .FindCircuitDefinition(child.Id)!;
        var childNot = child.ComponentInstances.Single();
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);
        await Apply(workspace, workspaceId, attached, new ConnectTerminalsIntent(
            [
                new DefinitionTerminalReference(child.Id, inputPort.Id),
                new InstanceTerminalReference(child.Id, childNot.Id, "A"),
            ]));
        await Apply(workspace, workspaceId, attached, new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(child.Id, childNot.Id, "Q"),
                new DefinitionTerminalReference(child.Id, outputPort.Id),
            ]));

        await Apply(workspace, workspaceId, attached, PlaceLibrary(
            mainId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0)));
        await Apply(workspace, workspaceId, attached, new PlaceComponentInstanceIntent(
            mainId,
            new CircuitDefinitionComponentTarget(child.Id),
            [],
            new ComponentPlacement(new GridPoint(4, 0)),
            "Inverter"));
        await Apply(workspace, workspaceId, attached, PlaceLibrary(
            mainId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0)));
        var main = (await Read(workspace, workspaceId, attached)).ProjectRevision.Document
            .EntryCircuitDefinition;
        var source = LibraryInstance(main, "source.input");
        var call = main.ComponentInstances.Single(instance =>
            instance.Target is CircuitDefinitionComponentTarget);
        var sink = LibraryInstance(main, "sink.output");
        await Apply(workspace, workspaceId, attached, new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(mainId, source.Id, "Q"),
                new InstanceTerminalReference(mainId, call.Id, inputPort.Id.Value),
            ]));
        await Apply(workspace, workspaceId, attached, new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(mainId, call.Id, outputPort.Id.Value),
                new InstanceTerminalReference(mainId, sink.Id, "D"),
            ]));

        var beforeCompilation = await Read(workspace, workspaceId, attached);
        var compiled = await workspace.DispatchAsync(
            new RequestCompilation(
                EditorWorkspaceTestDriver.Command(workspaceId, attached),
                EditorWorkspaceTestDriver.Compilation(beforeCompilation)),
            CancellationToken.None);
        var compiledProjection = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            workspaceId,
            attached,
            cancellationToken);
        var sessionCreated = await workspace.DispatchAsync(
            new CreateSession(
                EditorWorkspaceTestDriver.Command(workspaceId, attached),
                EditorWorkspaceTestDriver.SessionCreation(compiledProjection),
                SessionConfigurationV1.ForEntryOutputs(compiledProjection.ProjectRevision)),
            cancellationToken);
        var initial = await Read(workspace, workspaceId, attached);
        var scheduled = await workspace.DispatchAsync(
            EditorWorkspaceTestDriver.ScheduleInput(
                EditorWorkspaceTestDriver.Command(workspaceId, attached),
                EditorWorkspaceTestDriver.SessionMutation(initial),
                1,
                source.Id, [LogicValue.One]),
            cancellationToken);
        var scheduledProjection = await Read(workspace, workspaceId, attached);
        var stepped = await workspace.DispatchAsync(
            new StepSession(
                EditorWorkspaceTestDriver.Command(workspaceId, attached),
                EditorWorkspaceTestDriver.SessionMutation(scheduledProjection)),
            cancellationToken);
        var afterStep = await Read(workspace, workspaceId, attached);

        using (Assert.Multiple())
        {
            await Assert.That(compiled).IsTypeOf<CompilationAccepted>();
            await Assert.That(compiledProjection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(sessionCreated).IsTypeOf<SimulationSessionCreated>();
            await Assert.That(initial.Simulation!.Probes.Single().Value)
                .IsEquivalentTo([LogicValue.One]);
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
            await Assert.That(stepped).IsTypeOf<SessionStepped>();
            await Assert.That(afterStep.Simulation!.Probes.Single().Value)
                .IsEquivalentTo([LogicValue.Zero]);
        }
    }

    [Test]
    public async Task DispatchAsync_RepeatedChildStimulus_PreservesOccurrenceAndAtomicBatchIdentity(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(WorkspaceBuild.TestFingerprint);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Repeated input", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var workspaceId = opened.WorkspaceId;
        var attached = await EditorWorkspaceTestDriver.AttachAsync(workspace, workspaceId, cancellationToken);
        var mainId = opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, workspaceId, attached, new CreateCircuitDefinitionIntent("Cell", []));
        var child = (await Read(workspace, workspaceId, attached)).ProjectRevision.Document
            .CircuitDefinitions.Single(definition => definition.DisplayName == "Cell");
        await Apply(workspace, workspaceId, attached, PlaceLibrary(child.Id, "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("initialValue", new LogicVectorParameterValue([LogicValue.Zero])),
            ], new GridPoint(0, 0)));
        await Apply(workspace, workspaceId, attached, PlaceLibrary(child.Id, "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ], new GridPoint(4, 0)));
        child = (await Read(workspace, workspaceId, attached)).ProjectRevision.Document
            .FindCircuitDefinition(child.Id)!;
        var input = LibraryInstance(child, "source.input");
        var sink = LibraryInstance(child, "sink.output");
        await Apply(workspace, workspaceId, attached, new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(child.Id, input.Id, "Q"),
                new InstanceTerminalReference(child.Id, sink.Id, "D"),
            ]));
        foreach (var name in new[] { "Left", "Right" })
        {
            await Apply(workspace, workspaceId, attached, new PlaceComponentInstanceIntent(
                mainId, new CircuitDefinitionComponentTarget(child.Id), [],
                new ComponentPlacement(new GridPoint(name == "Left" ? 0 : 8, 0)), name));
        }

        var beforeCompilation = await Read(workspace, workspaceId, attached);
        var instances = beforeCompilation.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances;
        CompilationSource Source(string name) => new(
            new InstancePortSourceIdentity(child.Id, input.Id, "Q"),
            new HierarchyPath(mainId,
                [new HierarchyPathStep(mainId, instances.Single(instance => instance.DisplayName == name).Id)]));
        var left = Source("Left");
        var right = Source("Right");
        var netId = beforeCompilation.ProjectRevision.Document.FindCircuitDefinition(child.Id)!
            .Nets.Single().Id;
        CompilationSource Probe(CompilationSource driver) => new(
            new NetSourceIdentity(child.Id, netId), driver.HierarchyPath);
        await workspace.DispatchAsync(new RequestCompilation(
            EditorWorkspaceTestDriver.Command(workspaceId, attached),
            EditorWorkspaceTestDriver.Compilation(beforeCompilation)), cancellationToken);
        var compiled = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace, workspaceId, attached, cancellationToken);
        var created = await workspace.DispatchAsync(new CreateSession(
            EditorWorkspaceTestDriver.Command(workspaceId, attached),
            EditorWorkspaceTestDriver.SessionCreation(compiled),
            SessionConfigurationV1.ForWorkbench([Probe(left), Probe(right)])), cancellationToken);
        await Assert.That(created).IsTypeOf<SimulationSessionCreated>();
        var initial = await Read(workspace, workspaceId, attached);
        var context = EditorWorkspaceTestDriver.Command(workspaceId, attached, "child-stimulus");
        var precondition = EditorWorkspaceTestDriver.SessionMutation(initial);
        static StimulusAssignment Assign(CompilationSource source, LogicValue value) =>
            new(source, new LogicVector([value]));
        var command = new ScheduleStimulusBatch(context, precondition,
            new StimulusBatch(1, [Assign(left, LogicValue.One)]));

        var scheduled = await workspace.DispatchAsync(command, cancellationToken);
        var replay = await workspace.DispatchAsync(new ScheduleStimulusBatch(context, precondition,
            new StimulusBatch(1, [Assign(Source("Left"), LogicValue.One)])), cancellationToken);
        var differentOccurrence = (WorkspaceCommandRejected)await workspace.DispatchAsync(
            new ScheduleStimulusBatch(context, precondition,
                new StimulusBatch(1, [Assign(right, LogicValue.One)])), cancellationToken);
        var differentValue = (WorkspaceCommandRejected)await workspace.DispatchAsync(
            new ScheduleStimulusBatch(context, precondition,
                new StimulusBatch(1, [Assign(left, LogicValue.X)])), cancellationToken);
        var afterSchedule = await Read(workspace, workspaceId, attached);
        var firstStepCommand = new StepSession(
            EditorWorkspaceTestDriver.Command(workspaceId, attached),
            EditorWorkspaceTestDriver.SessionMutation(afterSchedule));
        var firstStep = (SessionStepped)await workspace.DispatchAsync(firstStepCommand, cancellationToken);
        var afterFirstStep = await Read(workspace, workspaceId, attached);

        var invalid = (WorkspaceCommandRejected)await workspace.DispatchAsync(new ScheduleStimulusBatch(
            EditorWorkspaceTestDriver.Command(workspaceId, attached),
            EditorWorkspaceTestDriver.SessionMutation(afterFirstStep),
            new StimulusBatch(2,
                [Assign(left, LogicValue.Zero), Assign(new CompilationSource(
                    new InstancePortSourceIdentity(child.Id, sink.Id, "D"), right.HierarchyPath), LogicValue.One)])),
            cancellationToken);
        var afterInvalid = await Read(workspace, workspaceId, attached);
        var batch = await workspace.DispatchAsync(new ScheduleStimulusBatch(
            EditorWorkspaceTestDriver.Command(workspaceId, attached),
            EditorWorkspaceTestDriver.SessionMutation(afterInvalid),
            new StimulusBatch(2, [Assign(left, LogicValue.Zero), Assign(right, LogicValue.One)])),
            cancellationToken);
        var afterBatch = await Read(workspace, workspaceId, attached);
        await workspace.DispatchAsync(new StepSession(
            EditorWorkspaceTestDriver.Command(workspaceId, attached),
            EditorWorkspaceTestDriver.SessionMutation(afterBatch)), cancellationToken);
        var afterSecondStep = await Read(workspace, workspaceId, attached);
        var replayedStep = await workspace.DispatchAsync(firstStepCommand, cancellationToken);
        var afterReplay = await Read(workspace, workspaceId, attached);

        using (Assert.Multiple())
        {
            await Assert.That(scheduled).IsTypeOf<StimulusScheduled>();
            await Assert.That(replay).IsEqualTo(scheduled);
            await Assert.That(differentOccurrence.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(differentValue.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(afterFirstStep.Simulation!.Probes.Select(probe => probe.Value.Single()))
                .IsEquivalentTo([LogicValue.One, LogicValue.Zero], CollectionOrdering.Matching);
            await Assert.That(firstStep.Advance.SessionVersion).IsEqualTo(afterFirstStep.Simulation.SessionVersion);
            await Assert.That(firstStep.Advance.TraceCursor).IsEqualTo(afterFirstStep.Simulation.TraceCursor);
            await Assert.That(firstStep.Advance.ObservedProbePatch.Select(probe => probe.ProbeId))
                .IsEquivalentTo([afterFirstStep.Simulation.Probes[0].ProbeId]);
            await Assert.That(invalid.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(afterInvalid.Simulation).IsEqualTo(afterFirstStep.Simulation);
            await Assert.That(afterInvalid.ProjectionVersion).IsEqualTo(afterFirstStep.ProjectionVersion);
            await Assert.That(batch).IsTypeOf<StimulusScheduled>();
            await Assert.That(afterSecondStep.Simulation!.LogicalTime).IsEqualTo(2UL);
            await Assert.That(afterSecondStep.Simulation.Probes.Select(probe => probe.Value.Single()))
                .IsEquivalentTo([LogicValue.Zero, LogicValue.One], CollectionOrdering.Matching);
            await Assert.That(replayedStep).IsEqualTo(firstStep);
            await Assert.That(firstStep.Advance.LogicalTime).IsEqualTo(1UL);
            await Assert.That(firstStep.Advance.ObservedProbePatch.Single().Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(afterReplay.Simulation).IsEqualTo(afterSecondStep.Simulation);
        }
    }

    private static async Task Apply(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached,
        EditIntent intent)
    {
        var projection = await Read(workspace, workspaceId, attached);
        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                EditorWorkspaceTestDriver.Command(workspaceId, attached),
                new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                intent),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached)
    {
        return ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(workspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None)).Projection;
    }

    private static PlaceComponentInstanceIntent PlaceLibrary(
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

    private static ComponentInstance LibraryInstance(
        CircuitDefinition definition,
        string contractId)
    {
        return definition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == contractId);
    }
}
