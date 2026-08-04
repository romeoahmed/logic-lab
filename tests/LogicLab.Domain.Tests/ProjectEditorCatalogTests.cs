using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

public sealed class ProjectEditorCatalogTests
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

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
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
        var originalIds = definition.Ports.Select(port => port.Id).ToArray();

        var outcome = ProjectEditor.Apply(
            revision,
            new MoveDefinitionPortsIntent(
                definition.Id,
                [
                    new DefinitionPortMove(
                        originalIds[0],
                        new DefinitionPortPlacement(
                            new GridPoint(-4, 7),
                            CardinalDirection.North)),
                    new DefinitionPortMove(
                        originalIds[1],
                        new DefinitionPortPlacement(
                            new GridPoint(12, 7),
                            CardinalDirection.South)),
                ]));

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        var ports = committed.Revision.Document.EntryCircuitDefinition.Ports;
        using (Assert.Multiple())
        {
            await Assert.That(ports.Select(port => port.Id).ToArray())
                .IsEquivalentTo(originalIds, CollectionOrdering.Matching);
            await Assert.That(ports[0].Placement.Facing).IsEqualTo(CardinalDirection.North);
            await Assert.That(ports[1].Placement.Facing).IsEqualTo(CardinalDirection.South);
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets)
                .IsEmpty();
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

        var sameShape = ProjectEditor.Apply(
            revision,
            new SetInstanceParametersIntent(
                definitionId,
                instanceId,
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "radix",
                        new ChoiceParameterValue("hex")),
                ]));
        var changedShape = ProjectEditor.Apply(
            revision,
            new SetInstanceParametersIntent(
                definitionId,
                instanceId,
                SinkParameters(2)));

        var committed = await Assert.That(sameShape).IsTypeOf<EditCommitted>();
        var rejected = await Assert.That(changedShape).IsTypeOf<EditRejected>();
        Assert.NotNull(committed);
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition
                .ComponentInstances.Single().Parameters)
                .IsEquivalentTo(
                    [
                        new ComponentParameterBinding(
                            "width",
                            new Unsigned32ParameterValue(1)),
                        new ComponentParameterBinding(
                            "radix",
                            new ChoiceParameterValue("hex")),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .Contains("authoring_invalid_parameter");
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

        var outcome = ProjectEditor.Apply(
            revision,
            new RemoveComponentInstancesIntent(
                definitionId,
                [.. instances.Select(instance => instance.Id)]));

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition
                .ComponentInstances).IsEmpty();
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets)
                .IsEmpty();
            await Assert.That(committed.RemovedSources
                .OfType<ComponentInstanceSourceIdentity>()).Count().IsEqualTo(2);
            await Assert.That(committed.RemovedSources
                .OfType<NetSourceIdentity>()).Count().IsEqualTo(1);
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

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
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
