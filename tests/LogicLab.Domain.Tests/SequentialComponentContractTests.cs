using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed class SequentialComponentContractTests
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
    public async Task FindContract_SequentialFamily_HasExactOrderedSchemas()
    {
        string[] contractIds =
        [
            "sequential.sr_latch",
            "sequential.d_latch",
            "sequential.dff",
            "sequential.jkff",
            "sequential.tff",
            "sequential.register",
            "sequential.shift_register",
            "sequential.counter",
        ];
        var actual = contractIds.Select(Project).ToArray();
        SequentialContractShape[] expected =
        [
            new("sequential.sr_latch", "initialState", "S,R,Q,QN", ""),
            new("sequential.d_latch", "width,initialState", "D,EN,Q", ""),
            new("sequential.dff", "width,edge,initialState", "D,CLK,Q", "edge=rising/falling"),
            new("sequential.jkff", "edge,initialState", "J,K,CLK,Q,QN", "edge=rising/falling"),
            new("sequential.tff", "edge,initialState", "T,CLK,Q,QN", "edge=rising/falling"),
            new("sequential.register", "width,edge,initialState", "D,CLK,EN,Q", "edge=rising/falling"),
            new(
                "sequential.shift_register",
                "width,direction,edge,initialState",
                "PARALLEL,SERIAL,LOAD,CLK,EN,Q,SERIAL_OUT",
                "direction=towardHigh/towardLow;edge=rising/falling"),
            new(
                "sequential.counter",
                "width,direction,edge,initialState",
                "LOAD_VALUE,LOAD,CLK,EN,Q,TERMINAL",
                "direction=up/down;edge=rising/falling"),
        ];

        await Assert.That(actual).IsEquivalentTo(
            expected,
            CollectionOrdering.Matching);
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

    private static SequentialContractShape Project(string contractId)
    {
        var contract = Find(contractId);
        return new SequentialContractShape(
            contractId,
            string.Join(',', contract.Parameters.Select(parameter => parameter.Id)),
            string.Join(',', contract.Ports.Select(port => port.Id)),
            string.Join(';', contract.Parameters
                .Where(parameter => parameter.AllowedValues.Count > 0)
                .Select(parameter =>
                    $"{parameter.Id}={string.Join('/', parameter.AllowedValues)}")));
    }

    private sealed record SequentialContractShape(
        string ContractId,
        string Parameters,
        string Ports,
        string Choices);
}
