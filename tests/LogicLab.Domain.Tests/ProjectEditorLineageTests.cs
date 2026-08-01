using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ProjectEditorLineageTests
{
    [Test]
    public async Task Apply_InputNotOutputSequence_ProducesImmutableAtomicProjectLineage()
    {
        var genesis = BeginProject();
        var placedInput = Place(
            genesis,
            "source.input",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0));
        var input = FindByContract(placedInput, "source.input");
        var placedNot = Place(
            placedInput,
            "logic.not",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
            ],
            new GridPoint(4, 0));
        var logicNot = FindByContract(placedNot, "logic.not");
        var placedOutput = Place(
            placedNot,
            "sink.output",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "radix",
                    new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0));
        var output = FindByContract(placedOutput, "sink.output");
        var definitionId = genesis.Document.EntryCircuitDefinition.Id;
        var connectedInput = Commit(ProjectEditor.Apply(
            placedOutput,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, input.Id, "Q"),
                    new InstanceTerminalReference(definitionId, logicNot.Id, "A"),
                ])));
        var firstNet = connectedInput.Document.EntryCircuitDefinition.Nets.Single();
        var connectedOutput = Commit(ProjectEditor.Apply(
            connectedInput,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
                    new InstanceTerminalReference(definitionId, output.Id, "D"),
                ])));
        var moved = Commit(ProjectEditor.Apply(
            connectedOutput,
            new MoveComponentInstancesIntent(
                definitionId,
                [
                    new ComponentMove(
                        logicNot.Id,
                        new ComponentPlacement(new GridPoint(5, 2))),
                    new ComponentMove(
                        input.Id,
                        new ComponentPlacement(new GridPoint(1, 2))),
                ])));
        var revisions = new[]
        {
            genesis,
            placedInput,
            placedNot,
            placedOutput,
            connectedInput,
            connectedOutput,
            moved,
        };

        var finalDefinition = moved.Document.EntryCircuitDefinition;
        var finalFirstNet = finalDefinition.FindNet(firstNet.Id);
        Assert.NotNull(finalFirstNet);

        using (Assert.Multiple())
        {
            await Assert.That(revisions.Select(revision => revision.RevisionId).Distinct().Count())
                .IsEqualTo(7);
            await Assert.That(revisions.All(
                revision => revision.Document.ProjectId == genesis.Document.ProjectId))
                .IsTrue();
            await Assert.That(revisions.Select(
                    revision => revision.Document.EntryCircuitDefinition.ComponentInstances.Count)
                .ToArray())
                .IsEquivalentTo([0, 1, 2, 3, 3, 3, 3], CollectionOrdering.Matching);
            await Assert.That(revisions.Select(
                    revision => revision.Document.EntryCircuitDefinition.Nets.Count)
                .ToArray())
                .IsEquivalentTo([0, 0, 0, 0, 1, 2, 2], CollectionOrdering.Matching);
            await Assert.That(finalDefinition.ComponentInstances.Select(instance => instance.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray())
                .IsEquivalentTo(
                    new[] { input.Id, logicNot.Id, output.Id }
                        .OrderBy(id => id.Value, StringComparer.Ordinal)
                        .ToArray(),
                    CollectionOrdering.Matching);
            await Assert.That(finalFirstNet.Terminals)
                .IsEquivalentTo(firstNet.Terminals, CollectionOrdering.Matching);
            await Assert.That(
                    finalDefinition.FindComponentInstance(input.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(1, 2));
            await Assert.That(
                    finalDefinition.FindComponentInstance(logicNot.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(5, 2));
            await Assert.That(
                    finalDefinition.FindComponentInstance(output.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(8, 0));
            await Assert.That(
                    connectedOutput.Document.EntryCircuitDefinition
                        .FindComponentInstance(input.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(0, 0));
            await Assert.That(
                    connectedOutput.Document.EntryCircuitDefinition
                        .FindComponentInstance(logicNot.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(4, 0));
        }
    }

    private static ProjectRevision BeginProject()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed(
            "Inverter",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return ((ProjectGenesisCommitted)outcome).Revision;
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
                revision.Document.EntryCircuitDefinition.Id,
                new ComponentContractKey("logiclab.core", contractId),
                parameters,
                new ComponentPlacement(origin))));
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }

    private static ComponentInstance FindByContract(
        ProjectRevision revision,
        string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.ContractKey.ContractId == contractId);
    }
}
