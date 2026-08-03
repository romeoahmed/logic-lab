using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class SteeringComponentContractTests
{
    [Test]
    public async Task Contracts_SteeringFamily_HasExactCanonicalOrder()
    {
        var contracts = CoreLibrarySchema.Contracts
            .Where(contract => contract.Key.ContractId.StartsWith("logic.", StringComparison.Ordinal))
            .Select(contract => contract.Key.ContractId)
            .ToArray();

        await Assert.That(contracts).IsEquivalentTo(
            [
                "logic.and",
                "logic.buffer",
                "logic.decoder",
                "logic.demux",
                "logic.mux",
                "logic.nand",
                "logic.nor",
                "logic.not",
                "logic.or",
                "logic.priority_encoder",
                "logic.tristate",
                "logic.xnor",
                "logic.xor",
            ],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task PreparePorts_Mux_GeneratesPowerOfTwoInputs()
    {
        var contract = Find("logic.mux");

        var ports = contract.ResolvePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(8)),
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(2)),
        ]);

        await Assert.That(ports.Select(port => (port.Id, port.Direction, port.Width)))
            .IsEquivalentTo(
                [
                    ("D0", PortDirection.Input, 8U),
                    ("D1", PortDirection.Input, 8U),
                    ("D2", PortDirection.Input, 8U),
                    ("D3", PortDirection.Input, 8U),
                    ("S", PortDirection.Input, 2U),
                    ("Q", PortDirection.Output, 8U),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task PreparePorts_Decoder_GeneratesOneBitOutputs()
    {
        var contract = Find("logic.decoder");

        var ports = contract.ResolvePorts(
        [
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(2)),
            new ComponentParameterBinding("enablePolarity", new ChoiceParameterValue("activeLow")),
        ]);

        await Assert.That(ports.Select(port => (port.Id, port.Direction, port.Width)))
            .IsEquivalentTo(
                [
                    ("A", PortDirection.Input, 2U),
                    ("EN", PortDirection.Input, 1U),
                    ("Q0", PortDirection.Output, 1U),
                    ("Q1", PortDirection.Output, 1U),
                    ("Q2", PortDirection.Output, 1U),
                    ("Q3", PortDirection.Output, 1U),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task PreparePorts_PriorityEncoder_ComputesMinimumBinaryWidth()
    {
        var contract = Find("logic.priority_encoder");

        var ports = contract.ResolvePorts(
        [
            new ComponentParameterBinding("inputCount", new Unsigned32ParameterValue(5)),
            new ComponentParameterBinding("priority", new ChoiceParameterValue("highestIndex")),
        ]);

        await Assert.That(ports.Select(port => (port.Id, port.Direction, port.Width)))
            .IsEquivalentTo(
                [
                    ("A0", PortDirection.Input, 1U),
                    ("A1", PortDirection.Input, 1U),
                    ("A2", PortDirection.Input, 1U),
                    ("A3", PortDirection.Input, 1U),
                    ("A4", PortDirection.Input, 1U),
                    ("Q", PortDirection.Output, 3U),
                    ("VALID", PortDirection.Output, 1U),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task PreparePorts_GateFanInBelowTwo_RejectsExactRule()
    {
        var contract = Find("logic.and");

        await Assert.That(() => contract.PreparePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(1)),
        ])).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Apply_GateFanInBelowTwo_ReturnsStructuredDiagnosticWithoutRevision()
    {
        var genesis = (ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Invalid gate",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));

        var outcome = ProjectEditor.Apply(
            genesis.Revision,
            new PlaceComponentInstanceIntent(
                genesis.Revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.and"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "fanIn",
                        new Unsigned32ParameterValue(1)),
                ],
                new ComponentPlacement(new GridPoint(0, 0))));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        var diagnostic = await Assert.That(rejected.Diagnostics).HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("authoring_invalid_parameter");
            await Assert.That(diagnostic.Arguments).Contains(argument =>
                argument.Name == "parameterId"
                && argument.Value is StableTokenDiagnosticValue { Value: "fanIn" });
            await Assert.That(diagnostic.Arguments).Contains(argument =>
                argument.Name == "rule"
                && argument.Value is StableTokenDiagnosticValue { Value: "minimumValue" });
            await Assert.That(genesis.Revision.Document.EntryCircuitDefinition
                .ComponentInstances).IsEmpty();
        }
    }

    private static ComponentContractSchema Find(string contractId)
    {
        return CoreLibrarySchema.FindContract(
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId))
            ?? throw new InvalidOperationException($"Missing contract {contractId}.");
    }
}
