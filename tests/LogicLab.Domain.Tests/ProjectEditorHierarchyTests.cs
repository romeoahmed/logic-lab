using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorHierarchyTests
{
    [Test]
    public async Task Apply_CreateDefinition_CommitsOrderedPublicContract()
    {
        var revision = BeginProject();
        var declarations = new[]
        {
            new DefinitionPortDeclaration(
                "A",
                PortDirection.Input,
                1,
                new DefinitionPortPlacement(new GridPoint(0, 2), CardinalDirection.West)),
            new DefinitionPortDeclaration(
                "Q",
                PortDirection.Output,
                1,
                new DefinitionPortPlacement(new GridPoint(8, 2), CardinalDirection.East)),
        };

        var outcome = ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Inverter", declarations));

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        var definition = committed.Revision.Document.CircuitDefinitions
            .Single(candidate => candidate.DisplayName == "Inverter");
        declarations[0] = new DefinitionPortDeclaration(
            "Changed",
            PortDirection.Output,
            8,
            new DefinitionPortPlacement(new GridPoint(99, 99), CardinalDirection.North));

        using (Assert.Multiple())
        {
            await Assert.That(definition.Ports.Select(port => port.DisplayName).ToArray())
                .IsEquivalentTo(["A", "Q"], CollectionOrdering.Matching);
            await Assert.That(definition.Ports.Select(port => port.Direction).ToArray())
                .IsEquivalentTo(
                    [PortDirection.Input, PortDirection.Output],
                    CollectionOrdering.Matching);
            await Assert.That(definition.Ports.All(port => port.Width == 1)).IsTrue();
            await Assert.That(definition.Ports.Select(port => port.Id).Distinct().Count())
                .IsEqualTo(2);
            await Assert.That(committed.ChangedSources)
                .Contains(new CircuitRootSourceIdentity(definition.Id));
            await Assert.That(committed.ChangedSources
                .OfType<DefinitionPortSourceIdentity>()
                .Select(source => source.DefinitionPortId)
                .ToArray())
                .IsEquivalentTo(
                    definition.Ports.Select(port => port.Id).ToArray(),
                    CollectionOrdering.Any);
        }
    }

    [Test]
    public async Task Apply_DefinitionInstanceAndBoundaryTerminals_CommitsHierarchy()
    {
        var revision = BeginProject();
        var (withChild, child) = CreateInverterDefinition(revision);
        var logicNotOutcome = ProjectEditor.Apply(
            withChild,
            new PlaceComponentInstanceIntent(
                child.Id,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 2)),
                "NOT"));
        await Assert.That(logicNotOutcome).IsTypeOf<EditCommitted>();
        var withNot = ((EditCommitted)logicNotOutcome).Revision;
        var logicNot = withNot.Document.FindCircuitDefinition(child.Id)!
            .ComponentInstances.Single();
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);

        var inputConnection = ProjectEditor.Apply(
            withNot,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(child.Id, inputPort.Id),
                    new InstanceTerminalReference(child.Id, logicNot.Id, "A"),
                ]));
        await Assert.That(inputConnection).IsTypeOf<EditCommitted>();
        var withInputConnection = ((EditCommitted)inputConnection).Revision;
        var outputConnection = ProjectEditor.Apply(
            withInputConnection,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(child.Id, logicNot.Id, "Q"),
                    new DefinitionTerminalReference(child.Id, outputPort.Id),
                ]));
        await Assert.That(outputConnection).IsTypeOf<EditCommitted>();
        var completedChild = ((EditCommitted)outputConnection).Revision;
        var placement = new ComponentPlacement(new GridPoint(6, 0));

        var instanceOutcome = ProjectEditor.Apply(
            completedChild,
            new PlaceComponentInstanceIntent(
                completedChild.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                placement,
                "Nested inverter"));

        var committed = await Assert.That(instanceOutcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        var instance = committed.Revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single();
        using (Assert.Multiple())
        {
            await Assert.That(instance.Target)
                .IsEqualTo(new CircuitDefinitionComponentTarget(child.Id));
            await Assert.That(instance.Parameters).IsEmpty();
            await Assert.That(instance.Placement).IsEqualTo(placement);
            await Assert.That(
                committed.Revision.Document.FindCircuitDefinition(child.Id)!.Nets
                    .SelectMany(net => net.Terminals)
                    .OfType<DefinitionTerminalReference>()
                    .Select(terminal => terminal.DefinitionPortId)
                    .ToArray())
                .IsEquivalentTo(
                    child.Ports.Select(port => port.Id).ToArray(),
                    CollectionOrdering.Any);
        }
    }

    [Test]
    public async Task Apply_SetEntryDefinition_ChangesEntryWithoutChangingIdentity()
    {
        var revision = BeginProject();
        var (withChild, child) = CreateInverterDefinition(revision);

        var outcome = ProjectEditor.Apply(
            withChild,
            new SetEntryCircuitDefinitionIntent(child.Id));

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinitionId)
                .IsEqualTo(child.Id);
            await Assert.That(committed.Revision.Document.ProjectId)
                .IsEqualTo(revision.Document.ProjectId);
            await Assert.That(committed.Revision.RevisionId == withChild.RevisionId)
                .IsFalse();
            await Assert.That(committed.Revision.Document.CircuitDefinitions)
                .Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task Apply_SetCurrentEntryDefinition_CommitsNewRevision()
    {
        var revision = BeginProject();

        var outcome = ProjectEditor.Apply(
            revision,
            new SetEntryCircuitDefinitionIntent(
                revision.Document.EntryCircuitDefinitionId));

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        var changedSource = await Assert.That(committed.ChangedSources).HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.RevisionId)
                .IsNotEqualTo(revision.RevisionId);
            await Assert.That(committed.Revision.Document.EntryCircuitDefinitionId)
                .IsEqualTo(revision.Document.EntryCircuitDefinitionId);
            await Assert.That(changedSource).IsEqualTo(
                new ProjectRootSourceIdentity(revision.Document.ProjectId));
            await Assert.That(committed.RemovedSources).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_InvalidDefinitionContract_RejectsWithoutRevision()
    {
        var revision = BeginProject();

        var outcome = ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "",
                [
                    new DefinitionPortDeclaration(
                        "",
                        (PortDirection)99,
                        0,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 0),
                            (CardinalDirection)99)),
                ]));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    [
                        "authoring_invalid_coordinate",
                        "authoring_invalid_text",
                        "authoring_invalid_text",
                        "authoring_invalid_width",
                        "authoring_missing_reference",
                    ],
                    CollectionOrdering.Any);
            await Assert.That(revision.Document.CircuitDefinitions).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Apply_DefinitionTargetWithParameters_RejectsAllTargetDefects()
    {
        var revision = BeginProject();
        var missingId = BeginProject().Document.EntryCircuitDefinitionId;

        var outcome = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(missingId),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0))));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_invalid_parameter", "authoring_missing_reference"],
                    CollectionOrdering.Matching);
            await Assert.That(revision.Document.EntryCircuitDefinition.ComponentInstances)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_DefinitionTerminalsFromWrongScopeAndWidth_RejectAtomically()
    {
        var revision = BeginProject();
        var outcome = ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Mixed widths",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 0),
                            CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        2,
                        new DefinitionPortPlacement(
                            new GridPoint(8, 0),
                            CardinalDirection.East)),
                ]));
        var withChild = ((EditCommitted)outcome).Revision;
        var child = withChild.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Mixed widths");
        var wrongScope = ProjectEditor.Apply(
            withChild,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(
                        withChild.Document.EntryCircuitDefinitionId,
                        child.Ports[0].Id),
                    new DefinitionTerminalReference(child.Id, child.Ports[1].Id),
                ]));
        var wrongWidth = ProjectEditor.Apply(
            withChild,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(child.Id, child.Ports[0].Id),
                    new DefinitionTerminalReference(child.Id, child.Ports[1].Id),
                ]));

        using (Assert.Multiple())
        {
            await Assert.That(((EditRejected)wrongScope).Diagnostics
                .All(diagnostic => diagnostic.Code == "authoring_missing_reference"))
                .IsTrue();
            await Assert.That(((EditRejected)wrongWidth).Diagnostics.Single().Code)
                .IsEqualTo("authoring_width_mismatch");
            await Assert.That(withChild.Document.FindCircuitDefinition(child.Id)!.Nets)
                .IsEmpty();
        }
    }

    private static (ProjectRevision Revision, CircuitDefinition Definition)
        CreateInverterDefinition(ProjectRevision revision)
    {
        var outcome = ProjectEditor.Apply(
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
                ]));
        var committed = (EditCommitted)outcome;
        var definition = committed.Revision.Document.CircuitDefinitions
            .Single(candidate => candidate.DisplayName == "Inverter");
        return (committed.Revision, definition);
    }

    private static ProjectRevision BeginProject()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed(
            "Hierarchy",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return ((ProjectGenesisCommitted)outcome).Revision;
    }
}
