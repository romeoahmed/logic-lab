using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

public sealed class AccessibleSceneProjectorTests
{
    private static readonly string[] ExpectedLabels = ["Input", "NOT", "Output"];
    private static readonly string[] ExpectedDefinitionPortLabels = ["A", "Q"];
    private static readonly PortDirection[] ExpectedDefinitionPortDirections =
        [PortDirection.Input, PortDirection.Output];

    [Test]
    public async Task Project_CompleteCircuit_ExposesReachableSemanticTopology()
    {
        var revision = CreateCompleteCircuit();

        var scene = AccessibleSceneProjector.Project(revision);

        using (Assert.Multiple())
        {
            await Assert.That(scene.DisplayName).IsEqualTo("Main");
            await Assert.That(scene.Components).Count().IsEqualTo(3);
            await Assert.That(scene.Components.Select(item => item.Label))
                .IsEquivalentTo(ExpectedLabels);
            await Assert.That(scene.Components.SelectMany(item => item.Ports)).Count()
                .IsEqualTo(4);
            await Assert.That(scene.Connections).Count().IsEqualTo(2);
            await Assert.That(scene.Connections.All(item => item.Terminals.Count == 2))
                .IsTrue();
            await Assert.That(scene.Components.All(item =>
                item.Source.CircuitDefinitionId == scene.CircuitDefinitionId))
                .IsTrue();
            await Assert.That(scene.Connections.All(item =>
                item.Source.CircuitDefinitionId == scene.CircuitDefinitionId))
                .IsTrue();
        }
    }

    [Test]
    public async Task Project_ComponentMove_PreservesElectricalTerminalMembership()
    {
        var revision = CreateCompleteCircuit();
        var before = AccessibleSceneProjector.Project(revision);
        var logicNot = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == "logic.not");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new MoveComponentInstancesIntent(
                revision.Document.EntryCircuitDefinitionId,
                [new ComponentMove(logicNot.Id, new ComponentPlacement(new GridPoint(20, 10)))])));

        var after = AccessibleSceneProjector.Project(revision);

        await Assert.That(after.Connections.SelectMany(item => item.Terminals))
            .IsEquivalentTo(before.Connections.SelectMany(item => item.Terminals));
        await Assert.That(after.Components.Single(item =>
            item.Source.ComponentInstanceId == logicNot.Id).Placement.Origin)
            .IsEqualTo(new GridPoint(20, 10));
    }

    [Test]
    public async Task Project_ExplicitTopology_ExposesJunctionsAndWireGeometry()
    {
        var revision = CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var net = definition.Nets[0];
        revision = Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                definition.Id,
                net.Id,
                new GridPoint(2, 1),
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(4, 1)]),
                    new UnroutedWireRoute(),
                ],
                [],
                [])));

        var scene = AccessibleSceneProjector.Project(revision);
        var connection = scene.Connections.Single(item => item.Source.NetId == net.Id);

        using (Assert.Multiple())
        {
            await Assert.That(connection.Junctions).Count().IsEqualTo(1);
            await Assert.That(connection.Junctions[0].Point)
                .IsEqualTo(new GridPoint(2, 1));
            await Assert.That(connection.Junctions[0].Source.CircuitDefinitionId)
                .IsEqualTo(scene.CircuitDefinitionId);
            await Assert.That(connection.WireGeometries).Count().IsEqualTo(2);
            await Assert.That(connection.WireGeometries.Any(
                item => item.Route is OrthogonalWireRoute)).IsTrue();
            await Assert.That(connection.WireGeometries.Any(
                item => item.Route is UnroutedWireRoute)).IsTrue();
            await Assert.That(connection.Terminals).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task Project_SelectedDefinition_ExposesBoundaryPortsAndDefinitionInstance()
    {
        var revision = CreateCompleteCircuit();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
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
                ])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Inverter");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(new GridPoint(12, 0)),
                "Nested inverter")));

        var childScene = AccessibleSceneProjector.Project(revision, child.Id);
        var mainScene = AccessibleSceneProjector.Project(
            revision,
            revision.Document.EntryCircuitDefinitionId);
        var call = mainScene.Components.Single(component =>
            component.Label == "Nested inverter");

        using (Assert.Multiple())
        {
            await Assert.That(childScene.CircuitDefinitionId).IsEqualTo(child.Id);
            await Assert.That(childScene.DefinitionPorts.Select(port => port.Label))
                .IsEquivalentTo(
                    ExpectedDefinitionPortLabels,
                    CollectionOrdering.Matching);
            await Assert.That(call.Ports.Select(port => port.Label))
                .IsEquivalentTo(
                    ExpectedDefinitionPortLabels,
                    CollectionOrdering.Matching);
            await Assert.That(call.Ports.Select(port => port.Direction))
                .IsEquivalentTo(ExpectedDefinitionPortDirections);
        }
    }

    private static ProjectRevision CreateCompleteCircuit()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Presentation fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Place(revision, "source.input", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ], new GridPoint(0, 0));
        var input = Find(revision, "source.input");
        revision = Place(revision, "logic.not", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        ], new GridPoint(4, 0));
        var logicNot = Find(revision, "logic.not");
        revision = Place(revision, "sink.output", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ], new GridPoint(8, 0));
        var output = Find(revision, "sink.output");
        revision = Connect(revision,
            new InstanceTerminalReference(definitionId, input.Id, "Q"),
            new InstanceTerminalReference(definitionId, logicNot.Id, "A"));
        return Connect(revision,
            new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
            new InstanceTerminalReference(definitionId, output.Id, "D"));
    }

    private static ProjectRevision Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(origin))));
    }

    private static ProjectRevision Connect(
        ProjectRevision revision,
        params InstanceTerminalReference[] terminals)
    {
        return Commit(ProjectEditor.Apply(revision, new ConnectTerminalsIntent(terminals)));
    }

    private static ComponentInstance Find(ProjectRevision revision, string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }
}
