using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ArithmeticComponentContractTests
{
    [Test]
    public async Task Contracts_ArithmeticFamily_HasExactCanonicalOrder()
    {
        var contracts = CoreLibrarySchema.Contracts
            .Where(contract => contract.Key.ContractId is
                "logic.unsigned_compare" or "logic.adder" or "logic.subtractor" or
                "logic.shift")
            .Select(contract => contract.Key.ContractId)
            .ToArray();

        await Assert.That(contracts).IsEquivalentTo(
            [
                "logic.adder",
                "logic.shift",
                "logic.subtractor",
                "logic.unsigned_compare",
            ],
            CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("logic.unsigned_compare", new[] { "A", "B", "LT", "EQ", "GT" })]
    [Arguments("logic.adder", new[] { "A", "B", "CIN", "SUM", "COUT" })]
    [Arguments("logic.subtractor", new[] { "A", "B", "BIN", "DIFF", "BOUT" })]
    public async Task ResolvePorts_FixedArithmeticContract_UsesExactPortOrder(
        string contractId,
        string[] expectedPortIds)
    {
        var ports = Resolve(contractId,
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(8)),
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(ports.Select(port => port.Id).ToArray())
                .IsEquivalentTo(expectedPortIds, CollectionOrdering.Matching);
            await Assert.That(ports.Where(port => port.Id is "A" or "B")
                    .Select(port => port.Width).ToArray())
                .IsEquivalentTo([8U, 8U], CollectionOrdering.Matching);
            await Assert.That(ports.Where(port =>
                    port.Id is "CIN" or "COUT" or "BIN" or "BOUT" or
                        "LT" or "EQ" or "GT")
                    .All(port => port.Width == 1U))
                .IsTrue();
        }
    }

    [Test]
    [Arguments(1U, 1U)]
    [Arguments(2U, 1U)]
    [Arguments(3U, 2U)]
    [Arguments(4U, 2U)]
    [Arguments(5U, 3U)]
    [Arguments(uint.MaxValue, 32U)]
    public async Task ResolvePorts_Shift_ComputesCheckedAmountWidth(
        uint width,
        uint expectedAmountWidth)
    {
        var ports = Resolve("logic.shift",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("direction", new ChoiceParameterValue("left")),
        ]);

        await Assert.That(ports.Select(port => (port.Id, port.Width)).ToArray())
            .IsEquivalentTo(
                [("D", width), ("AMOUNT", expectedAmountWidth), ("Q", width)],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task ResolvePorts_ShiftUnknownDirection_RejectsAtContractBoundary()
    {
        var contract = Find("logic.shift");

        await Assert.That(() => contract.ResolvePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(8)),
            new ComponentParameterBinding("direction", new ChoiceParameterValue("rotate")),
        ])).ThrowsExactly<ArgumentException>();
    }

    private static ResolvedComponentPortSchema[] Resolve(
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var resolution = Find(contractId).ResolvePorts(parameters);
        if (!resolution.TryMaterialize(32, out var ports))
        {
            throw new InvalidOperationException("The test Port set must fit its fixed budget.");
        }

        return ports.ToArray();
    }

    private static ComponentContractSchema Find(string contractId)
    {
        return CoreLibrarySchema.FindContract(
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId))
            ?? throw new InvalidOperationException($"Missing contract {contractId}.");
    }
}
