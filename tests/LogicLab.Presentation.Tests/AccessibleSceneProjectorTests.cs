using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

internal sealed class AccessibleSceneProjectorTests
{
    [Test]
    [Arguments("logic.unsigned_compare")]
    [Arguments("logic.adder")]
    [Arguments("logic.subtractor")]
    [Arguments("logic.shift")]
    public async Task Project_ArithmeticComponent_ExposesResolvedPortContract(
        string contractId)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Arithmetic projection",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var parameters = contractId == "logic.shift"
            ? new[]
            {
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(5)),
                new ComponentParameterBinding(
                    "direction",
                    new ChoiceParameterValue("right")),
            }
            :
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(5)),
            ];
        revision = Place(revision, contractId, parameters, new GridPoint(0, 0));

        var instance = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        var component = Project(revision).Components.Single();
        var expectedPorts = ResolvePorts(instance);

        await Assert.That(component.Ports.Select(port =>
                (port.Source.PortId, port.Direction, port.Width)))
            .IsEquivalentTo(expectedPorts, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TryProject_GeneratedPortShapeCannotBeMaterialized_ReturnsFalse()
    {
        var revision = CreateCompleteCircuit();
        var outcome = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.mux"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "selectorWidth",
                        new Unsigned32ParameterValue(31)),
                ],
                new ComponentPlacement(new GridPoint(24, 0))));
        revision = Commit(outcome);

        var projected = AccessibleSceneProjector.TryProject(
            revision,
            maximumPortCount: ulong.MaxValue,
            out var scene);

        using (Assert.Multiple())
        {
            await Assert.That(projected).IsFalse();
            await Assert.That(scene).IsNull();
        }
    }

    [Test]
    public async Task TryProject_TotalPortCountBeyondBudget_ReturnsFalseWithoutProjection()
    {
        var projected = AccessibleSceneProjector.TryProject(
            CreateCompleteCircuit(),
            maximumPortCount: 3,
            out var scene);

        using (Assert.Multiple())
        {
            await Assert.That(projected).IsFalse();
            await Assert.That(scene).IsNull();
        }
    }

    [Test]
    public async Task Project_CompleteCircuit_PreservesAuthoredTopology()
    {
        var revision = CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;

        var scene = Project(revision);

        using (Assert.Multiple())
        {
            await Assert.That(scene.CircuitDefinitionId).IsEqualTo(definition.Id);
            await Assert.That(scene.DisplayName).IsEqualTo("Main");
            await Assert.That(scene.Components).Count()
                .IsEqualTo(definition.ComponentInstances.Count);
            await Assert.That(scene.Connections).Count().IsEqualTo(definition.Nets.Count);
            await Assert.That(scene.Components.Select(item => item.Source).Distinct()).Count()
                .IsEqualTo(scene.Components.Count);
            await Assert.That(scene.Connections.Select(item => item.Source).Distinct()).Count()
                .IsEqualTo(scene.Connections.Count);
        }

        foreach (var instance in definition.ComponentInstances)
        {
            var projected = scene.Components.Single(component =>
                component.Source.ComponentInstanceId == instance.Id);
            using (Assert.Multiple())
            {
                await Assert.That(projected.Source)
                    .IsEqualTo(new ComponentInstanceSourceIdentity(definition.Id, instance.Id));
                await Assert.That(projected.Placement).IsEqualTo(instance.Placement);
                await Assert.That(projected.Ports.Select(port =>
                        (port.Source.PortId, port.Direction, port.Width)))
                    .IsEquivalentTo(ExpectedPorts(instance), CollectionOrdering.Matching);
                await Assert.That(projected.Ports.All(port =>
                    port.Source.CircuitDefinitionId == definition.Id
                    && port.Source.ComponentInstanceId == instance.Id)).IsTrue();
            }
        }

        foreach (var net in definition.Nets)
        {
            var projected = scene.Connections.Single(connection =>
                connection.Source.NetId == net.Id);
            using (Assert.Multiple())
            {
                await Assert.That(projected.Source)
                    .IsEqualTo(new NetSourceIdentity(definition.Id, net.Id));
                await Assert.That(projected.Width).IsEqualTo(net.Width);
                await Assert.That(projected.Terminals)
                    .IsEquivalentTo(net.Terminals, CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task Project_ComponentMove_PreservesElectricalTerminalMembership()
    {
        var revision = CreateCompleteCircuit();
        var before = Project(revision);
        var logicNot = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == "logic.not");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new MoveComponentInstancesIntent(
                revision.Document.EntryCircuitDefinitionId,
                [new ComponentMove(logicNot.Id, new ComponentPlacement(new GridPoint(20, 10)))])));

        var after = Project(revision);

        using (Assert.Multiple())
        {
            await Assert.That(after.Components.Select(component => component.Source))
                .IsEquivalentTo(before.Components.Select(component => component.Source));
            await Assert.That(after.Connections.Select(connection => connection.Source))
                .IsEquivalentTo(before.Connections.Select(connection => connection.Source));
        }

        foreach (var beforeComponent in before.Components)
        {
            var afterComponent = after.Components.Single(component =>
                component.Source == beforeComponent.Source);
            var expectedPlacement = beforeComponent.Source.ComponentInstanceId == logicNot.Id
                ? new ComponentPlacement(new GridPoint(20, 10))
                : beforeComponent.Placement;
            using (Assert.Multiple())
            {
                await Assert.That(afterComponent.Label).IsEqualTo(beforeComponent.Label);
                await Assert.That(afterComponent.Ports)
                    .IsEquivalentTo(beforeComponent.Ports, CollectionOrdering.Matching);
                await Assert.That(afterComponent.Placement).IsEqualTo(expectedPlacement);
            }
        }

        foreach (var beforeConnection in before.Connections)
        {
            var afterConnection = after.Connections.Single(connection =>
                connection.Source == beforeConnection.Source);
            using (Assert.Multiple())
            {
                await Assert.That(afterConnection.Width).IsEqualTo(beforeConnection.Width);
                await Assert.That(afterConnection.Terminals)
                    .IsEquivalentTo(beforeConnection.Terminals, CollectionOrdering.Matching);
            }
        }
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

        var projectedDefinition = revision.Document.EntryCircuitDefinition;
        var scene = Project(revision);
        var connection = scene.Connections.Single(item => item.Source.NetId == net.Id);
        var junction = await Assert.That(connection.Junctions).HasSingleItem();
        var authoredJunction = await Assert.That(projectedDefinition.Junctions).HasSingleItem();

        using (Assert.Multiple())
        {
            await Assert.That(junction.Source)
                .IsEqualTo(new JunctionSourceIdentity(
                    projectedDefinition.Id,
                    authoredJunction.Id));
            await Assert.That(junction.NetSource).IsEqualTo(connection.Source);
            await Assert.That(junction.Point).IsEqualTo(new GridPoint(2, 1));
            await Assert.That(connection.WireGeometries).Count()
                .IsEqualTo(projectedDefinition.WireGeometries.Count);
            await Assert.That(connection.Terminals).Count().IsEqualTo(2);
        }

        foreach (var authoredGeometry in projectedDefinition.WireGeometries)
        {
            var geometry = await Assert.That(connection.WireGeometries)
                .HasSingleItem(item => item.Source.WireGeometryId == authoredGeometry.Id);
            using (Assert.Multiple())
            {
                await Assert.That(geometry.Source).IsEqualTo(
                    new WireGeometrySourceIdentity(projectedDefinition.Id, authoredGeometry.Id));
                await Assert.That(geometry.NetSource).IsEqualTo(connection.Source);
            }

            if (authoredGeometry.Route is OrthogonalWireRoute expectedOrthogonal)
            {
                var actualOrthogonal = (await Assert.That(geometry.Route)
                    .IsTypeOf<OrthogonalWireRoute>())!;
                await Assert.That(actualOrthogonal.Points)
                    .IsEquivalentTo(expectedOrthogonal.Points, CollectionOrdering.Matching);
            }
            else
            {
                await Assert.That(geometry.Route).IsTypeOf<UnroutedWireRoute>();
            }
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

        var mainDefinition = revision.Document.EntryCircuitDefinition;
        var authoredCall = mainDefinition.ComponentInstances.Single(instance =>
            instance.Target is CircuitDefinitionComponentTarget target
            && target.CircuitDefinitionId == child.Id);
        var childScene = Project(revision, child.Id);
        var mainScene = Project(
            revision,
            revision.Document.EntryCircuitDefinitionId);
        var call = mainScene.Components.Single(component =>
            component.Label == "Nested inverter");
        var expectedDefinitionPorts = child.Ports.Select(port =>
                (port.Id.Value, port.DisplayName, port.Direction, port.Width, port.Placement))
            .ToArray();
        var expectedCallPorts = child.Ports.Select(port =>
                (port.Id.Value, port.DisplayName, port.Direction))
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(childScene.CircuitDefinitionId).IsEqualTo(child.Id);
            await Assert.That(childScene.DefinitionPorts.Select(port =>
                    (port.Source.DefinitionPortId.Value,
                        port.Label,
                        port.Direction,
                        port.Width,
                        port.Placement)))
                .IsEquivalentTo(expectedDefinitionPorts, CollectionOrdering.Matching);
            await Assert.That(childScene.DefinitionPorts.All(port =>
                port.Source.CircuitDefinitionId == child.Id)).IsTrue();
            await Assert.That(call.Source).IsEqualTo(
                new ComponentInstanceSourceIdentity(mainDefinition.Id, authoredCall.Id));
            await Assert.That(call.Label).IsEqualTo("Nested inverter");
            await Assert.That(call.Placement).IsEqualTo(authoredCall.Placement);
            await Assert.That(call.Ports.Select(port =>
                    (port.Source.PortId, port.Label, port.Direction)))
                .IsEquivalentTo(expectedCallPorts, CollectionOrdering.Matching);
            await Assert.That(call.Ports.All(port =>
                port.Source.CircuitDefinitionId == mainDefinition.Id
                && port.Source.ComponentInstanceId == authoredCall.Id)).IsTrue();
        }
    }

    private static AccessibleSceneProjection Project(ProjectRevision revision)
    {
        return AccessibleSceneProjector.TryProject(revision, 10_000, out var scene)
            ? scene
            : throw new InvalidOperationException(
                "The bounded test Scene could not be projected.");
    }

    private static AccessibleSceneProjection Project(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId)
    {
        return AccessibleSceneProjector.TryProject(
                revision,
                circuitDefinitionId,
                10_000,
                out var scene)
            ? scene
            : throw new InvalidOperationException(
                "The bounded test Scene could not be projected.");
    }

    private static (string PortId, PortDirection Direction, uint Width)[] ExpectedPorts(
        ComponentInstance instance)
    {
        return ResolvePorts(instance);
    }

    private static (string PortId, PortDirection Direction, uint Width)[] ResolvePorts(
        ComponentInstance instance)
    {
        var target = instance.Target as LibraryComponentTarget
            ?? throw new InvalidOperationException("Expected a library component target.");
        var contract = CoreLibrarySchema.FindContract(target.ContractKey)
            ?? throw new InvalidOperationException($"Missing contract {target.ContractKey}.");
        return contract.ResolvePorts(instance.Parameters)
            .TryMaterialize(10_000, out var ports)
            ? [.. ports.Select(port => (port.Id, port.Direction, port.Width))]
            : throw new InvalidOperationException(
                $"The bounded test ports for {target.ContractKey} could not be materialized.");
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
