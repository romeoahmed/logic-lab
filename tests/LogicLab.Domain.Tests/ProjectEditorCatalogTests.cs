using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorCatalogTests
{
    [Test]
    public async Task Apply_RenameDefinitionAndInstance_PreservesIdentities()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("logic.not"),
                WidthParameters(1),
                new ComponentPlacement(new GridPoint(1, 2)),
                "Old instance")));
        var instanceId = revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Id;

        revision = Commit(ProjectEditor.Apply(
            revision,
            new RenameCircuitDefinitionIntent(definitionId, "Renamed definition")));
        var outcome = ProjectEditor.Apply(
            revision,
            new RenameComponentInstanceIntent(
                definitionId,
                instanceId,
                "Renamed instance"));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        var definition = committed.Revision.Document.EntryCircuitDefinition;
        using (Assert.Multiple())
        {
            await Assert.That(definition.Id).IsEqualTo(definitionId);
            await Assert.That(definition.DisplayName).IsEqualTo("Renamed definition");
            await Assert.That(definition.ComponentInstances.Single().Id)
                .IsEqualTo(instanceId);
            await Assert.That(definition.ComponentInstances.Single().DisplayName)
                .IsEqualTo("Renamed instance");
        }
    }

    [Test]
    public async Task Apply_MoveDefinitionPorts_ChangesPresentationOnly()
    {
        var revision = BeginProjectWithPorts();
        var definition = revision.Document.EntryCircuitDefinition;
        var originalPorts = definition.Ports.Select(port =>
            (port.Id, port.DisplayName, port.Direction, port.Width)).ToArray();
        AuthoredTerminalReference[] terminals =
        [
            .. definition.Ports.Select(port =>
                new DefinitionTerminalReference(definition.Id, port.Id)),
        ];
        revision = Commit(ProjectEditor.Apply(revision, new ConnectTerminalsIntent(terminals)));
        var originalNet = revision.Document.EntryCircuitDefinition.Nets.Single();
        DefinitionPortMove[] moves =
        [
            new(
                definition.Ports[0].Id,
                new DefinitionPortPlacement(new GridPoint(-4, 7), CardinalDirection.North)),
            new(
                definition.Ports[1].Id,
                new DefinitionPortPlacement(new GridPoint(12, 7), CardinalDirection.South)),
        ];

        var outcome = ProjectEditor.Apply(
            revision,
            new MoveDefinitionPortsIntent(definition.Id, moves));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        var ports = committed.Revision.Document.EntryCircuitDefinition.Ports;
        var net = committed.Revision.Document.EntryCircuitDefinition.Nets.Single();
        using (Assert.Multiple())
        {
            await Assert.That(ports.Select(port =>
                    (port.Id, port.DisplayName, port.Direction, port.Width)))
                .IsEquivalentTo(originalPorts, CollectionOrdering.Matching);
            await Assert.That(ports.Select(port => port.Placement))
                .IsEquivalentTo(moves.Select(move => move.Placement), CollectionOrdering.Matching);
            await Assert.That(net.Id).IsEqualTo(originalNet.Id);
            await Assert.That(net.Width).IsEqualTo(1U);
            await Assert.That(net.Terminals).IsEquivalentTo(terminals, CollectionOrdering.Matching);
            await Assert.That(committed.ChangedSources).IsEquivalentTo(
                definition.Ports.Select(port => (AuthoredSourceIdentity)
                    new DefinitionPortSourceIdentity(definition.Id, port.Id)));
            await Assert.That(committed.RemovedSources).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_SetInstanceParameters_RequiresIdenticalResolvedPorts()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                SinkParameters(1),
                new ComponentPlacement(new GridPoint(0, 0)))));
        var instanceId = revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Id;
        ComponentParameterBinding[] parameters =
        [
            new("width", new Unsigned32ParameterValue(1)),
            new("radix", new ChoiceParameterValue("hex")),
        ];

        var sameShape = ProjectEditor.Apply(
            revision,
            new SetInstanceParametersIntent(
                definitionId,
                instanceId,
                parameters));
        var changedShape = ProjectEditor.Apply(
            revision,
            new SetInstanceParametersIntent(
                definitionId,
                instanceId,
                SinkParameters(2)));

        var committed = (await Assert.That(sameShape).IsTypeOf<EditCommitted>())!;
        var rejected = (await Assert.That(changedShape).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition
                .ComponentInstances.Single().Parameters)
                .IsEquivalentTo(parameters, CollectionOrdering.Matching);
            await Assert.That(rejected.Diagnostics.Select(item => item.Code))
                .IsEquivalentTo(["authoring_invalid_parameter"]);
            await Assert.That(revision.Document.EntryCircuitDefinition.ComponentInstances
                .Single().Parameters)
                .IsEquivalentTo(SinkParameters(1), CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_RemoveInstances_RemovesTerminalMembershipAndEmptyNet()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("source.constant"),
                ConstantParameters(LogicValue.One),
                new ComponentPlacement(new GridPoint(0, 0)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                SinkParameters(1),
                new ComponentPlacement(new GridPoint(4, 0)))));
        var instances = revision.Document.EntryCircuitDefinition.ComponentInstances;
        var source = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId
                == "source.constant");
        var sink = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId
                == "sink.output");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, source.Id, "Q"),
                    new InstanceTerminalReference(definitionId, sink.Id, "D"),
                ])));
        var netId = revision.Document.EntryCircuitDefinition.Nets.Single().Id;

        var outcome = ProjectEditor.Apply(
            revision,
            new RemoveComponentInstancesIntent(
                definitionId,
                [.. instances.Select(instance => instance.Id)]));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        AuthoredSourceIdentity[] removedSources =
        [
            new ComponentInstanceSourceIdentity(definitionId, source.Id),
            new ComponentInstanceSourceIdentity(definitionId, sink.Id),
            new NetSourceIdentity(definitionId, netId),
        ];
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition
                .ComponentInstances).IsEmpty();
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets)
                .IsEmpty();
            await Assert.That(committed.RemovedSources).IsEquivalentTo(removedSources);
            await Assert.That(committed.ChangedSources).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_RemoveDefinitionWithDependent_RejectsWithoutRevision()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Child", [])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.Id != revision.Document.EntryCircuitDefinitionId);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(new GridPoint(0, 0)))));

        var outcome = ProjectEditor.Apply(
            revision,
            new RemoveCircuitDefinitionIntent(child.Id));

        var rejected = (await Assert.That(outcome).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("authoring_delete_has_dependents");
            await Assert.That(revision.Document.CircuitDefinitions).Count().IsEqualTo(2);
        }
    }

    private static ProjectRevision BeginProject()
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Catalog fixture",
            LibrarySnapshot.Core,
            TeachingMixedProfile(),
            "Main"))).Revision;
    }

    private static ProjectRevision BeginProjectWithPorts()
    {
        var revision = BeginProject();
        var outcome = ProjectEditor.Apply(
            revision,
            new ChangePublicPortContractIntent(
                revision.Document.EntryCircuitDefinitionId,
                [
                    new NewDefinitionPortContract(new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 0),
                            CardinalDirection.West))),
                    new NewDefinitionPortContract(new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(8, 0),
                            CardinalDirection.East))),
                ],
                []));
        return Commit(outcome);
    }

}
