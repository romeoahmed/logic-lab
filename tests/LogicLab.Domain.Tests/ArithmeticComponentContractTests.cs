using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Domain.Tests;

internal readonly record struct PositiveUInt32(uint Value);

internal static class ArithmeticComponentContractArbitraries
{
    private static readonly uint[] BoundaryWidths =
    [
        1,
        2,
        3,
        7,
        8,
        9,
        15,
        16,
        17,
        31,
        32,
        33,
        byte.MaxValue,
        (uint)byte.MaxValue + 1,
        ushort.MaxValue,
        (uint)ushort.MaxValue + 1,
        int.MaxValue,
        (uint)int.MaxValue + 1,
        uint.MaxValue,
    ];

    public static Arbitrary<PositiveUInt32> PositiveWidth()
    {
        var generator = Gen.Frequency(
                (4, Gen.Elements(BoundaryWidths)),
                (6, ArbMap.Default.GeneratorFor<uint>().Where(static width => width > 0)))
            .Select(static width => new PositiveUInt32(width));

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<PositiveUInt32> Shrink(PositiveUInt32 sample)
    {
        if (sample.Value <= 1)
        {
            yield break;
        }

        yield return new PositiveUInt32(1);
        var halfWidth = sample.Value / 2;
        if (halfWidth > 1)
        {
            yield return new PositiveUInt32(halfWidth);
        }

        foreach (var boundary in BoundaryWidths.Where(width =>
                     width > 1 && width < sample.Value && width != halfWidth))
        {
            yield return new PositiveUInt32(boundary);
        }
    }
}

internal sealed class ArithmeticComponentContractTests
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

    [Test, FsCheckProperty(
        Arbitrary = new[] { typeof(ArithmeticComponentContractArbitraries) })]
    public Property ResolvePorts_Shift_AnyPositiveUIntWidth_ComputesMinimumAmountWidth(
        PositiveUInt32 positiveWidth)
    {
        var width = positiveWidth.Value;
        var expectedAmountWidth = width == 1
            ? 1U
            : (uint)BitOperations.Log2(width - 1) + 1;
        var ports = Resolve("logic.shift",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("direction", new ChoiceParameterValue("left")),
        ]);
        var actualAmountWidth = ports.Single(port => port.Id == "AMOUNT").Width;

        return ports.Select(port => (port.Id, port.Direction, port.Width))
            .SequenceEqual(
                [
                    ("D", PortDirection.Input, width),
                    ("AMOUNT", PortDirection.Input, expectedAmountWidth),
                    ("Q", PortDirection.Output, width),
                ])
            .Label(
                $"width={width}, expected amount width={expectedAmountWidth}, " +
                $"actual={actualAmountWidth}")
            .Collect(width switch
            {
                1 => "width=1",
                <= byte.MaxValue => "width=2..255",
                <= ushort.MaxValue => "width=256..65535",
                _ => "width>=65536",
            });
    }

    [Test]
    public async Task ResolvePorts_Shift_UInt32Maximum_UsesThirtyTwoBitAmount()
    {
        var ports = Resolve("logic.shift",
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(uint.MaxValue)),
            new ComponentParameterBinding("direction", new ChoiceParameterValue("right")),
        ]);

        await Assert.That(ports.Single(port => port.Id == "AMOUNT").Width)
            .IsEqualTo(32U);
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
