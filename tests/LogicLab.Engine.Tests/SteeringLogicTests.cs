using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class SteeringLogicTests
{
    [Test]
    [Arguments(SimulationEvaluatorKind.LogicAnd, LogicValue.Zero)]
    [Arguments(SimulationEvaluatorKind.LogicNand, LogicValue.One)]
    [Arguments(SimulationEvaluatorKind.LogicOr, LogicValue.One)]
    [Arguments(SimulationEvaluatorKind.LogicNor, LogicValue.Zero)]
    [Arguments(SimulationEvaluatorKind.LogicXor, LogicValue.X)]
    [Arguments(SimulationEvaluatorKind.LogicXnor, LogicValue.X)]
    public async Task Gate_ThreeInputs_UsesFourStateFold(
        SimulationEvaluatorKind kind,
        LogicValue expected)
    {
        var result = CombinationalEvaluation.Gate(
            kind,
            [Vector(LogicValue.One), Vector(LogicValue.X), Vector(LogicValue.Zero)]);

        await Assert.That(result[0]).IsEqualTo(expected);
    }

    [Test]
    public async Task TriState_UnknownEnable_MergesEnabledAndDisabledContributions()
    {
        var result = CombinationalEvaluation.TriState(
            Vector(LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.Z),
            LogicValue.X,
            activeHigh: true);

        await Assert.That(Values(result)).IsEquivalentTo(
            [LogicValue.X, LogicValue.X, LogicValue.X, LogicValue.X],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Mux_UnknownSelector_MergesOnlyReachableArms()
    {
        var result = CombinationalEvaluation.Mux(
            [
                Vector(LogicValue.Zero, LogicValue.One),
                Vector(LogicValue.One, LogicValue.One),
                Vector(LogicValue.Zero, LogicValue.Zero),
                Vector(LogicValue.One, LogicValue.Zero),
            ],
            Vector(LogicValue.X, LogicValue.Zero));

        await Assert.That(Values(result)).IsEquivalentTo(
            [LogicValue.X, LogicValue.One],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Demux_UnknownSelector_MergesReachableOutputCases()
    {
        var outputs = CombinationalEvaluation.Demux(
            Vector(LogicValue.One),
            Vector(LogicValue.X, LogicValue.Zero));

        await Assert.That(outputs.Select(output => output[0]).ToArray())
            .IsEquivalentTo(
                [LogicValue.X, LogicValue.X, LogicValue.Zero, LogicValue.Zero],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task Decoder_UnknownEnableAndAddress_MergesReachableOneHotVectors()
    {
        var outputs = CombinationalEvaluation.Decoder(
            Vector(LogicValue.X),
            LogicValue.X,
            activeHigh: true);

        await Assert.That(outputs.Select(output => output[0]).ToArray())
            .IsEquivalentTo(
                [LogicValue.X, LogicValue.X],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task PriorityEncoder_UnknownHigherPriorityInput_MergesIndexAndValidity()
    {
        var result = CombinationalEvaluation.PriorityEncoder(
            [LogicValue.One, LogicValue.Zero, LogicValue.X],
            lowestIndex: false);

        using (Assert.Multiple())
        {
            await Assert.That(Values(result.Index)).IsEquivalentTo(
                [LogicValue.Zero, LogicValue.X],
                CollectionOrdering.Matching);
            await Assert.That(result.Valid).IsEqualTo(LogicValue.One);
        }
    }

    private static LogicVector Vector(params LogicValue[] values) => new(values);

    private static LogicValue[] Values(LogicVector vector)
    {
        return [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
    }
}
