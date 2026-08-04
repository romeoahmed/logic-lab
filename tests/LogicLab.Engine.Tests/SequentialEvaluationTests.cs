using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class SequentialEvaluationTests
{
    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, false)]
    [Arguments(LogicValue.One, LogicValue.Zero, LogicValue.Zero, LogicValue.One, false)]
    [Arguments(LogicValue.Zero, LogicValue.One, LogicValue.One, LogicValue.Zero, false)]
    [Arguments(LogicValue.One, LogicValue.One, LogicValue.Zero, LogicValue.X, true)]
    public async Task SrLatch_DefiniteControls_ImplementsTruthTable(
        LogicValue set,
        LogicValue reset,
        LogicValue current,
        LogicValue expected,
        bool expectedConflict)
    {
        var result = SequentialEvaluation.SrLatch(current, set, reset);

        using (Assert.Multiple())
        {
            await Assert.That(result.State[0]).IsEqualTo(expected);
            await Assert.That(result.HasControlConflict).IsEqualTo(expectedConflict);
        }
    }

    [Test]
    public async Task SrLatch_UnknownSet_MergesHoldAndSetCases()
    {
        var result = SequentialEvaluation.SrLatch(
            LogicValue.Zero,
            LogicValue.X,
            LogicValue.Zero);

        using (Assert.Multiple())
        {
            await Assert.That(result.State[0]).IsEqualTo(LogicValue.X);
            await Assert.That(result.HasControlConflict).IsFalse();
        }
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.Zero, LogicValue.Zero, LogicValue.One)]
    [Arguments(LogicValue.Zero, LogicValue.One, LogicValue.One, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.One, LogicValue.Zero, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.One, LogicValue.One, LogicValue.Zero)]
    public async Task JkFlipFlop_DefiniteControls_ImplementsTruthTable(
        LogicValue j,
        LogicValue k,
        LogicValue current,
        LogicValue expected)
    {
        var result = SequentialEvaluation.JkFlipFlop(current, j, k);

        await Assert.That(result[0]).IsEqualTo(expected);
    }

    [Test]
    public async Task JkFlipFlop_UnknownControls_MergesEveryReachableTransition()
    {
        var result = SequentialEvaluation.JkFlipFlop(
            LogicValue.Zero,
            LogicValue.X,
            LogicValue.X);

        await Assert.That(result[0]).IsEqualTo(LogicValue.X);
    }

    [Test]
    public async Task TFlipFlop_UnknownControl_MergesHoldAndToggleCases()
    {
        var result = SequentialEvaluation.TFlipFlop(
            LogicValue.Zero,
            LogicValue.X);

        await Assert.That(result[0]).IsEqualTo(LogicValue.X);
    }

    [Test]
    public async Task ShiftRegister_Directions_EnterAndRemoveOppositeEndBits()
    {
        var current = Vector(LogicValue.One, LogicValue.Zero, LogicValue.Zero);

        var towardHigh = SequentialEvaluation.ShiftRegister(
            current,
            Vector(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero),
            LogicValue.Zero,
            LogicValue.Zero,
            LogicValue.One,
            towardHigh: true);
        var towardLow = SequentialEvaluation.ShiftRegister(
            current,
            Vector(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero),
            LogicValue.Zero,
            LogicValue.Zero,
            LogicValue.One,
            towardHigh: false);

        using (Assert.Multiple())
        {
            await Assert.That(Bits(towardHigh)).IsEquivalentTo(
                [LogicValue.Zero, LogicValue.One, LogicValue.Zero],
                CollectionOrdering.Matching);
            await Assert.That(Bits(towardLow)).IsEquivalentTo(
                [LogicValue.Zero, LogicValue.Zero, LogicValue.Zero],
                CollectionOrdering.Matching);
            await Assert.That(SequentialEvaluation.ShiftSerialOutput(current, true))
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(SequentialEvaluation.ShiftSerialOutput(current, false))
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task ShiftRegister_UnknownLoadAndEnable_MergesAllReachableCases()
    {
        var result = SequentialEvaluation.ShiftRegister(
            Vector(LogicValue.Zero, LogicValue.Zero),
            Vector(LogicValue.One, LogicValue.One),
            LogicValue.One,
            LogicValue.X,
            LogicValue.X,
            towardHigh: true);

        await Assert.That(Bits(result)).IsEquivalentTo(
            [LogicValue.X, LogicValue.X],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task ShiftRegister_LoadAndEnableActive_LoadTakesPriorityOverShift()
    {
        var result = SequentialEvaluation.ShiftRegister(
            Vector(LogicValue.Zero, LogicValue.Zero),
            Vector(LogicValue.One, LogicValue.One),
            LogicValue.Zero,
            LogicValue.One,
            LogicValue.One,
            towardHigh: true);

        await Assert.That(Bits(result)).IsEquivalentTo(
            [LogicValue.One, LogicValue.One],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Counter_KnownState_CountsModuloWidthInBothDirections()
    {
        var up = SequentialEvaluation.Counter(
            Vector(LogicValue.One, LogicValue.One),
            Vector(LogicValue.Zero, LogicValue.Zero),
            LogicValue.Zero,
            LogicValue.One,
            countUp: true);
        var down = SequentialEvaluation.Counter(
            Vector(LogicValue.Zero, LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.Zero),
            LogicValue.Zero,
            LogicValue.One,
            countUp: false);

        using (Assert.Multiple())
        {
            await Assert.That(Bits(up)).IsEquivalentTo(
                [LogicValue.Zero, LogicValue.Zero],
                CollectionOrdering.Matching);
            await Assert.That(Bits(down)).IsEquivalentTo(
                [LogicValue.One, LogicValue.One],
                CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Counter_UnknownLoadAndEnable_MergesLoadCountAndHoldCases()
    {
        var result = SequentialEvaluation.Counter(
            Vector(LogicValue.Zero, LogicValue.Zero),
            Vector(LogicValue.One, LogicValue.One),
            LogicValue.X,
            LogicValue.X,
            countUp: true);

        await Assert.That(Bits(result)).IsEquivalentTo(
            [LogicValue.X, LogicValue.X],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Counter_LoadAndEnableActive_LoadTakesPriorityOverCount()
    {
        var result = SequentialEvaluation.Counter(
            Vector(LogicValue.Zero, LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.One),
            LogicValue.One,
            LogicValue.One,
            countUp: true);

        await Assert.That(Bits(result)).IsEquivalentTo(
            [LogicValue.Zero, LogicValue.One],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task CounterTerminal_UnknownState_ReturnsOnlyProvableResult()
    {
        using (Assert.Multiple())
        {
            await Assert.That(SequentialEvaluation.CounterTerminal(
                    Vector(LogicValue.One, LogicValue.One),
                    countUp: true))
                .IsEqualTo(LogicValue.One);
            await Assert.That(SequentialEvaluation.CounterTerminal(
                    Vector(LogicValue.Zero, LogicValue.X),
                    countUp: true))
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(SequentialEvaluation.CounterTerminal(
                    Vector(LogicValue.One, LogicValue.X),
                    countUp: true))
                .IsEqualTo(LogicValue.X);
        }
    }

    private static LogicVector Vector(params LogicValue[] values) => new(values);

    private static LogicValue[] Bits(LogicVector value) =>
        [.. Enumerable.Range(0, value.Width).Select(bit => value[bit])];
}
