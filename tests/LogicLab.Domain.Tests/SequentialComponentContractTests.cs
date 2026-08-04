using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class SequentialComponentContractTests
{
    [Test]
    public async Task FindContract_ClockSource_HasExactSchema()
    {
        var contract = Find("source.clock");

        using (Assert.Multiple())
        {
            await Assert.That(contract.Parameters.Select(parameter => (
                    parameter.Id,
                    parameter.Kind)))
                .IsEquivalentTo(
                    [
                        ("initialValue", ComponentParameterKind.BinaryLogicValue),
                        ("firstTransition", ComponentParameterKind.PositiveUnsigned64),
                        ("highDuration", ComponentParameterKind.PositiveUnsigned64),
                        ("lowDuration", ComponentParameterKind.PositiveUnsigned64),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(contract.Ports.Select(port => (
                    port.Id,
                    port.Direction,
                    port.WidthSource)))
                .IsEquivalentTo(
                    [("Q", PortDirection.Output, ComponentPortWidthSource.FixedOne)],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments(LogicValue.X, 1UL, 1UL, 1UL)]
    [Arguments(LogicValue.Zero, 0UL, 1UL, 1UL)]
    [Arguments(LogicValue.Zero, 1UL, 0UL, 1UL)]
    [Arguments(LogicValue.Zero, 1UL, 1UL, 0UL)]
    public async Task ResolvePorts_ClockSourceInvalidParameter_RejectsAtBoundary(
        LogicValue initialValue,
        ulong firstTransition,
        ulong highDuration,
        ulong lowDuration)
    {
        var contract = Find("source.clock");

        await Assert.That(() => contract.ResolvePorts(
        [
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([initialValue])),
            new ComponentParameterBinding(
                "firstTransition",
                new Unsigned64ParameterValue(firstTransition)),
            new ComponentParameterBinding(
                "highDuration",
                new Unsigned64ParameterValue(highDuration)),
            new ComponentParameterBinding(
                "lowDuration",
                new Unsigned64ParameterValue(lowDuration)),
        ])).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task FindContract_FirstSequentialFamily_HasExactOrderedSchemas()
    {
        var latch = Find("sequential.d_latch");
        var dff = Find("sequential.dff");
        var register = Find("sequential.register");

        using (Assert.Multiple())
        {
            await Assert.That(latch.Parameters.Select(parameter => parameter.Id))
                .IsEquivalentTo(["width", "initialState"], CollectionOrdering.Matching);
            await Assert.That(latch.Ports.Select(port => port.Id))
                .IsEquivalentTo(["D", "EN", "Q"], CollectionOrdering.Matching);
            await Assert.That(dff.Parameters.Select(parameter => parameter.Id))
                .IsEquivalentTo(
                    ["width", "edge", "initialState"],
                    CollectionOrdering.Matching);
            await Assert.That(dff.Ports.Select(port => port.Id))
                .IsEquivalentTo(["D", "CLK", "Q"], CollectionOrdering.Matching);
            await Assert.That(register.Parameters.Select(parameter => parameter.Id))
                .IsEquivalentTo(
                    ["width", "edge", "initialState"],
                    CollectionOrdering.Matching);
            await Assert.That(register.Ports.Select(port => port.Id))
                .IsEquivalentTo(["D", "CLK", "EN", "Q"], CollectionOrdering.Matching);
            await Assert.That(dff.Parameters[1].AllowedValues)
                .IsEquivalentTo(["rising", "falling"], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task FindContract_RemainingSequentialFamily_HasExactOrderedSchemas()
    {
        var srLatch = Find("sequential.sr_latch");
        var jkff = Find("sequential.jkff");
        var tff = Find("sequential.tff");
        var shiftRegister = Find("sequential.shift_register");
        var counter = Find("sequential.counter");

        using (Assert.Multiple())
        {
            await Assert.That(srLatch.Parameters.Select(item => item.Id))
                .IsEquivalentTo(["initialState"], CollectionOrdering.Matching);
            await Assert.That(srLatch.Ports.Select(item => item.Id))
                .IsEquivalentTo(["S", "R", "Q", "QN"], CollectionOrdering.Matching);
            await Assert.That(jkff.Parameters.Select(item => item.Id))
                .IsEquivalentTo(["edge", "initialState"], CollectionOrdering.Matching);
            await Assert.That(jkff.Ports.Select(item => item.Id))
                .IsEquivalentTo(["J", "K", "CLK", "Q", "QN"], CollectionOrdering.Matching);
            await Assert.That(tff.Parameters.Select(item => item.Id))
                .IsEquivalentTo(["edge", "initialState"], CollectionOrdering.Matching);
            await Assert.That(tff.Ports.Select(item => item.Id))
                .IsEquivalentTo(["T", "CLK", "Q", "QN"], CollectionOrdering.Matching);
            await Assert.That(shiftRegister.Parameters.Select(item => item.Id))
                .IsEquivalentTo(
                    ["width", "direction", "edge", "initialState"],
                    CollectionOrdering.Matching);
            await Assert.That(shiftRegister.Ports.Select(item => item.Id))
                .IsEquivalentTo(
                    ["PARALLEL", "SERIAL", "LOAD", "CLK", "EN", "Q", "SERIAL_OUT"],
                    CollectionOrdering.Matching);
            await Assert.That(counter.Parameters.Select(item => item.Id))
                .IsEquivalentTo(
                    ["width", "direction", "edge", "initialState"],
                    CollectionOrdering.Matching);
            await Assert.That(counter.Ports.Select(item => item.Id))
                .IsEquivalentTo(
                    ["LOAD_VALUE", "LOAD", "CLK", "EN", "Q", "TERMINAL"],
                    CollectionOrdering.Matching);
            await Assert.That(shiftRegister.Parameters[1].AllowedValues)
                .IsEquivalentTo(
                    ["towardHigh", "towardLow"],
                    CollectionOrdering.Matching);
            await Assert.That(counter.Parameters[1].AllowedValues)
                .IsEquivalentTo(["up", "down"], CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments("sequential.d_latch")]
    [Arguments("sequential.dff")]
    [Arguments("sequential.register")]
    [Arguments("sequential.sr_latch")]
    [Arguments("sequential.jkff")]
    [Arguments("sequential.tff")]
    [Arguments("sequential.shift_register")]
    [Arguments("sequential.counter")]
    public async Task ResolvePorts_HighImpedanceInitialState_RejectsStoredValue(
        string contractId)
    {
        ComponentParameterBinding[] parameters = contractId switch
        {
            "sequential.d_latch" =>
            [
                new("width", new Unsigned32ParameterValue(1)),
                new("initialState", new LogicVectorParameterValue([LogicValue.Z])),
            ],
            "sequential.sr_latch" =>
            [
                new("initialState", new LogicVectorParameterValue([LogicValue.Z])),
            ],
            "sequential.jkff" or "sequential.tff" =>
            [
                new("edge", new ChoiceParameterValue("rising")),
                new("initialState", new LogicVectorParameterValue([LogicValue.Z])),
            ],
            "sequential.shift_register" =>
            [
                new("width", new Unsigned32ParameterValue(1)),
                new("direction", new ChoiceParameterValue("towardHigh")),
                new("edge", new ChoiceParameterValue("rising")),
                new("initialState", new LogicVectorParameterValue([LogicValue.Z])),
            ],
            "sequential.counter" =>
            [
                new("width", new Unsigned32ParameterValue(1)),
                new("direction", new ChoiceParameterValue("up")),
                new("edge", new ChoiceParameterValue("rising")),
                new("initialState", new LogicVectorParameterValue([LogicValue.Z])),
            ],
            _ =>
            [
                new("width", new Unsigned32ParameterValue(1)),
                new("edge", new ChoiceParameterValue("rising")),
                new("initialState", new LogicVectorParameterValue([LogicValue.Z])),
            ],
        };

        await Assert.That(() => Find(contractId).ResolvePorts(parameters))
            .ThrowsExactly<ArgumentException>();
    }

    private static ComponentContractSchema Find(string contractId)
    {
        return CoreLibrarySchema.FindContract(new ComponentContractKey(
            CoreLibrarySchema.LibraryId,
            contractId)) ?? throw new InvalidOperationException(
                $"The {contractId} contract is missing.");
    }
}
