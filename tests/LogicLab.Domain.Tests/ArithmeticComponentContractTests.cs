using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ArithmeticComponentContractTests
{
    [Test]
    [Arguments(
        "logic.unsigned_compare",
        new[] { "A", "B", "LT", "EQ", "GT" },
        new uint[] { 8, 8, 1, 1, 1 },
        2)]
    [Arguments(
        "logic.adder",
        new[] { "A", "B", "CIN", "SUM", "COUT" },
        new uint[] { 8, 8, 1, 8, 1 },
        3)]
    [Arguments(
        "logic.subtractor",
        new[] { "A", "B", "BIN", "DIFF", "BOUT" },
        new uint[] { 8, 8, 1, 8, 1 },
        3)]
    public async Task ResolvePorts_FixedArithmeticContract_ProducesExactSchema(
        string contractId,
        string[] expectedPortIds,
        uint[] expectedWidths,
        int inputCount)
    {
        var ports = Resolve(contractId,
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(8)),
        ]);
        var expected = expectedPortIds.Select((id, index) => (
            Id: id,
            Direction: index < inputCount ? PortDirection.Input : PortDirection.Output,
            Width: expectedWidths[index]));

        await Assert.That(ports.Select(port => (port.Id, port.Direction, port.Width)))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
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

        await Assert.That(ports.Select(port => (port.Id, port.Direction, port.Width)))
            .IsEquivalentTo(
                [
                    ("D", PortDirection.Input, width),
                    ("AMOUNT", PortDirection.Input, expectedAmountWidth),
                    ("Q", PortDirection.Output, width),
                ],
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

        return [.. ports];
    }

    private static ComponentContractSchema Find(string contractId)
    {
        return CoreLibrarySchema.FindContract(
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId))
            ?? throw new InvalidOperationException($"Missing contract {contractId}.");
    }
}
