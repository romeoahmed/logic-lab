using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Pages;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

public sealed class EditorTopologyPartitionTests
{
    [Test]
    public async Task Editor_CreateSampleTopologyPartitions_ComponentCreationOrder_DoesNotChangeElectricalPairs()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Reverse-order fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = WebTestCircuit.Place(revision, "sink.output", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ], new GridPoint(8, 0));
        revision = WebTestCircuit.Place(revision, "logic.not", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        ], new GridPoint(4, 0));
        revision = WebTestCircuit.Place(revision, "source.input", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ], new GridPoint(0, 0));
        var input = WebTestCircuit.Find(revision, "source.input");
        var logicNot = WebTestCircuit.Find(revision, "logic.not");
        var output = WebTestCircuit.Find(revision, "sink.output");
        revision = WebTestCircuit.Connect(revision,
            new InstanceTerminalReference(definitionId, input.Id, "Q"),
            new InstanceTerminalReference(definitionId, logicNot.Id, "A"));
        revision = WebTestCircuit.Connect(revision,
            new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
            new InstanceTerminalReference(definitionId, output.Id, "D"));
        var beforeMerge = revision.Document.EntryCircuitDefinition;
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new MergeNetsIntent(
                definitionId,
                beforeMerge.Nets[0].Id,
                [beforeMerge.Nets[1].Id])));
        var definition = revision.Document.EntryCircuitDefinition;

        var partitions = Editor.CreateSampleTopologyPartitions(definition, definition.Nets.Single());
        var actualPairs = partitions
            .Select(partition => string.Join(
                "|",
                partition.Terminals
                    .OfType<InstanceTerminalReference>()
                    .Select(terminal =>
                        $"{((LibraryComponentTarget)definition.FindComponentInstance(
                            terminal.ComponentInstanceId)!.Target).ContractKey.ContractId}.{terminal.PortId}")
                    .Order(StringComparer.Ordinal)))
            .ToArray();

        await Assert.That(actualPairs).IsEquivalentTo(
            ["logic.not.A|source.input.Q", "logic.not.Q|sink.output.D"],
            CollectionOrdering.Matching);
    }
}
