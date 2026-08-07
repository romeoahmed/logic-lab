using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorMigrationTests
{
    [Test]
    public async Task Apply_ChangePublicPortContract_RemovesObsoleteBoundaryTerminal()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ChangePublicPortContractIntent(
                definitionId,
                [
                    new NewDefinitionPortContract(Port("A", PortDirection.Input, 1)),
                    new NewDefinitionPortContract(Port("Q", PortDirection.Output, 1)),
                ],
                [])));
        var definition = revision.Document.EntryCircuitDefinition;
        var input = definition.Ports.Single(port => port.Direction == PortDirection.Input);
        var output = definition.Ports.Single(port => port.Direction == PortDirection.Output);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(definitionId, input.Id),
                    new DefinitionTerminalReference(definitionId, output.Id),
                ])));
        var netId = revision.Document.EntryCircuitDefinition.Nets.Single().Id;

        var outcome = ProjectEditor.Apply(
            revision,
            new ChangePublicPortContractIntent(
                definitionId,
                [
                    new RetainedDefinitionPortContract(
                        input.Id,
                        Port("A", PortDirection.Input, 1)),
                ],
                []));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets
                .Single().Terminals)
                .IsEquivalentTo(
                    [
                        (AuthoredTerminalReference)new DefinitionTerminalReference(
                            definitionId,
                            input.Id),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(committed.ChangedSources)
                .Contains(new NetSourceIdentity(definitionId, netId));
            await Assert.That(committed.RemovedSources)
                .Contains(new DefinitionPortSourceIdentity(definitionId, output.Id));
        }
    }

    [Test]
    public async Task Apply_ChangePublicPortContract_DisconnectsCallSiteAndReportsRemovedNet()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Child",
                [Port("A", PortDirection.Input, 1), Port("Q", PortDirection.Output, 1)])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.Id != revision.Document.EntryCircuitDefinitionId);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(new GridPoint(0, 0)))));
        var parent = revision.Document.EntryCircuitDefinition;
        var callSite = parent.ComponentInstances.Single();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(parent.Id, callSite.Id, child.Ports[0].Id.Value),
                    new InstanceTerminalReference(parent.Id, callSite.Id, child.Ports[1].Id.Value),
                ])));
        var removedNetId = revision.Document.EntryCircuitDefinition.Nets.Single().Id;

        var outcome = ProjectEditor.Apply(
            revision,
            new ChangePublicPortContractIntent(
                child.Id,
                [],
                [
                    new CallSiteTerminalMigration(
                        parent.Id,
                        callSite.Id,
                        [
                            new PortTerminalMigration(child.Ports[0].Id, null),
                            new PortTerminalMigration(child.Ports[1].Id, null),
                        ]),
                ]));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets).IsEmpty();
            await Assert.That(committed.RemovedSources)
                .Contains(new NetSourceIdentity(parent.Id, removedNetId));
        }
    }

    [Test]
    public async Task Apply_ChangePublicPortContract_MigratesEveryCallSiteAtomically()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Child",
                [Port("A", PortDirection.Input, 1), Port("Q", PortDirection.Output, 1)])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.Id != revision.Document.EntryCircuitDefinitionId);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(new GridPoint(2, 2)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                Contract("source.constant"),
                ConstantParameters(LogicValue.One),
                new ComponentPlacement(new GridPoint(0, 0)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                Contract("sink.output"),
                SinkParameters(1),
                new ComponentPlacement(new GridPoint(6, 0)))));
        var parent = revision.Document.EntryCircuitDefinition;
        var callSite = parent.ComponentInstances.Single(instance =>
            instance.Target is CircuitDefinitionComponentTarget);
        var source = parent.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "source.constant");
        var sink = parent.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "sink.output");
        var oldInput = child.Ports[0];
        var oldOutput = child.Ports[1];
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(parent.Id, source.Id, "Q"),
                    new InstanceTerminalReference(parent.Id, callSite.Id, oldInput.Id.Value),
                ])));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(parent.Id, callSite.Id, oldOutput.Id.Value),
                    new InstanceTerminalReference(parent.Id, sink.Id, "D"),
                ])));
        var sourceRevision = revision;

        var outcome = ProjectEditor.Apply(
            revision,
            new ChangePublicPortContractIntent(
                child.Id,
                [
                    new RetainedDefinitionPortContract(
                        oldInput.Id,
                        Port("D", PortDirection.Input, 1)),
                    new NewDefinitionPortContract(
                        Port("Y", PortDirection.Output, 1)),
                ],
                [
                    new CallSiteTerminalMigration(
                        revision.Document.EntryCircuitDefinitionId,
                        callSite.Id,
                        [
                            new PortTerminalMigration(oldInput.Id, 0),
                            new PortTerminalMigration(oldOutput.Id, 1),
                        ]),
                ]));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        var changedChild = committed.Revision.Document.FindCircuitDefinition(child.Id)!;
        var changedCallSite = committed.Revision.Document.EntryCircuitDefinition
            .FindComponentInstance(callSite.Id)!;
        using (Assert.Multiple())
        {
            await Assert.That(changedChild.Ports[0].Id).IsEqualTo(oldInput.Id);
            await Assert.That(changedChild.Ports[0].DisplayName).IsEqualTo("D");
            await Assert.That(changedChild.Ports[1].Id).IsNotEqualTo(oldOutput.Id);
            await Assert.That(changedCallSite.Target)
                .IsEqualTo(new CircuitDefinitionComponentTarget(child.Id));
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets
                .SelectMany(net => net.Terminals)
                .OfType<InstanceTerminalReference>()
                .Any(terminal => terminal.ComponentInstanceId == callSite.Id
                    && terminal.PortId == changedChild.Ports[1].Id.Value)).IsTrue();
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.Nets
                .SelectMany(net => net.Terminals)
                .OfType<InstanceTerminalReference>()
                .Any(terminal => terminal.ComponentInstanceId == callSite.Id
                    && terminal.PortId == oldOutput.Id.Value)).IsFalse();
            await Assert.That(sourceRevision.Document.FindCircuitDefinition(child.Id)!.Ports[1].Id)
                .IsEqualTo(oldOutput.Id);
            await Assert.That(committed.RemovedSources)
                .Contains(new DefinitionPortSourceIdentity(child.Id, oldOutput.Id));
        }
    }

    [Test]
    public async Task Apply_ChangePublicPortContract_IncompleteCallSiteMigrationRejects()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Child",
                [Port("A", PortDirection.Input, 1)])));
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
            new ChangePublicPortContractIntent(
                child.Id,
                [new NewDefinitionPortContract(Port("B", PortDirection.Input, 1))],
                []));

        var rejected = (await Assert.That(outcome).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .Contains("authoring_missing_reference");
            await Assert.That(revision.Document.FindCircuitDefinition(child.Id)!.Ports[0].DisplayName)
                .IsEqualTo("A");
        }
    }

    [Test]
    public async Task Apply_ChangeInstanceContract_MigratesCompatibleTerminalMembership()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("logic.not"),
                WidthParameters(1),
                new ComponentPlacement(new GridPoint(0, 0)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                SinkParameters(1),
                new ComponentPlacement(new GridPoint(4, 0)))));
        var not = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "logic.not");
        var sink = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "sink.output");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, not.Id, "Q"),
                    new InstanceTerminalReference(definitionId, sink.Id, "D"),
                ])));

        var outcome = ProjectEditor.Apply(
            revision,
            new ChangeInstanceContractIntent(
                definitionId,
                not.Id,
                new LibraryComponentTarget(Contract("logic.buffer")),
                WidthParameters(1),
                [new InstancePortMigration("A", "A"), new InstancePortMigration("Q", "Q")],
                null));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        var changed = committed.Revision.Document.EntryCircuitDefinition;
        using (Assert.Multiple())
        {
            var changedInstance = changed.FindComponentInstance(not.Id)!;
            await Assert.That(((LibraryComponentTarget)changedInstance.Target)
                .ContractKey.ContractId).IsEqualTo("logic.buffer");
            await Assert.That(changed.Nets.Single().Terminals)
                .Contains(new InstanceTerminalReference(definitionId, not.Id, "Q"));
        }
    }

    private static ProjectRevision BeginProject()
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Migration fixture",
            LibrarySnapshot.Core,
            TeachingMixedProfile(),
            "Main"))).Revision;
    }

    private static DefinitionPortDeclaration Port(
        string name,
        PortDirection direction,
        uint width)
    {
        return new DefinitionPortDeclaration(
            name,
            direction,
            width,
            new DefinitionPortPlacement(
                new GridPoint(direction == PortDirection.Input ? 0 : 8, 0),
                direction == PortDirection.Input
                    ? CardinalDirection.West
                    : CardinalDirection.East));
    }
}
