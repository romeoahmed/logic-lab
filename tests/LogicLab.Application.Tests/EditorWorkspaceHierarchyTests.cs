using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceHierarchyTests
{
    [Test]
    public async Task DispatchAsync_HierarchicalCircuit_CompilesAndSimulatesAcrossBoundary(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Hierarchy project", "Main"),
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
                EditorWorkspaceTestDriver.SessionCreation(compiledProjection)),
            cancellationToken);
        var initial = await Read(workspace, workspaceId, attached);
        var scheduled = await workspace.DispatchAsync(
            new ScheduleInputStimulus(
                EditorWorkspaceTestDriver.Command(workspaceId, attached),
                EditorWorkspaceTestDriver.SessionMutation(initial),
                1,
                [new InputStimulusAssignment(source.Id, [LogicValue.One])]),
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
