using System.Collections.ObjectModel;
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
    public async Task ResolvePorts_Mux_GeneratesPowerOfTwoInputs()
    {
        var contract = Find("logic.mux");

        var ports = Materialize(contract.ResolvePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(8)),
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(2)),
        ]));

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
    public async Task TryGetPortCount_PowerOfTwoShapeBeyondUInt64_ReturnsFalse()
    {
        var contract = Find("logic.mux");

        var resolution = contract.ResolvePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(64)),
        ]);

        await Assert.That(resolution.TryGetPortCount(out _)).IsFalse();
    }

    [Test]
    public async Task TryMaterialize_PowerOfTwoShapeBeyondBudget_ReturnsFalseWithoutPorts()
    {
        var contract = Find("logic.mux");
        var resolution = contract.ResolvePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(30)),
        ]);

        var materialized = resolution.TryMaterialize(10_000, out var ports);

        using (Assert.Multiple())
        {
            await Assert.That(materialized).IsFalse();
            await Assert.That(ports).IsEmpty();
        }
    }

    [Test]
    public async Task TryResolvePort_PowerOfTwoShapeBeyondBudget_ResolvesOnePortWithoutExpansion()
    {
        var contract = Find("logic.mux");
        ComponentParameterBinding[] parameters =
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(8)),
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(30)),
        ];

        var resolved = contract.TryResolvePort(
            parameters,
            "D1073741823",
            out var port);
        var outOfRange = contract.TryResolvePort(
            parameters,
            "D1073741824",
            out _);
        var nonCanonical = contract.TryResolvePort(parameters, "D01", out _);

        using (Assert.Multiple())
        {
            await Assert.That(resolved).IsTrue();
            await Assert.That(outOfRange).IsFalse();
            await Assert.That(nonCanonical).IsFalse();
            await Assert.That((port!.Id, port.Direction, port.Width))
                .IsEqualTo(("D1073741823", PortDirection.Input, 8U));
        }
    }

    [Test]
    public async Task ResolvePorts_Decoder_GeneratesOneBitOutputs()
    {
        var contract = Find("logic.decoder");

        var ports = Materialize(contract.ResolvePorts(
        [
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(2)),
            new ComponentParameterBinding("enablePolarity", new ChoiceParameterValue("activeLow")),
        ]));

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
    public async Task ResolvePorts_PriorityEncoder_ComputesMinimumBinaryWidth()
    {
        var contract = Find("logic.priority_encoder");

        var ports = Materialize(contract.ResolvePorts(
        [
            new ComponentParameterBinding("inputCount", new Unsigned32ParameterValue(5)),
            new ComponentParameterBinding("priority", new ChoiceParameterValue("highestIndex")),
        ]));

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
    public async Task ResolvePorts_GateFanInBelowTwo_RejectsExactRule()
    {
        var contract = Find("logic.and");

        await Assert.That(() => contract.ResolvePorts(
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

    private static ReadOnlyCollection<ResolvedComponentPortSchema> Materialize(
        ComponentPortResolution resolution)
    {
        return resolution.TryMaterialize(100, out var ports)
            ? ports
            : throw new InvalidOperationException(
                "The bounded test Port resolution could not be materialized.");
    }
}
