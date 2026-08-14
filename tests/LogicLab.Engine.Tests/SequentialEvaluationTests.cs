using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using static LogicLab.Engine.Tests.FourStateTestData;

namespace LogicLab.Engine.Tests;

internal sealed class SequentialEvaluationTests
{
    [Test]
    public async Task SrLatch_EveryFourStateInput_MatchesReachableControlWorlds()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(3))
        {
            var actual = SequentialEvaluation.SrLatch(
                inputs[0], inputs[1], inputs[2]);
            var expected = Merge(BinaryWorlds(inputs).Select(world =>
                world[1] && world[2]
                    ? LogicValue.X
                    : Boolean(world[1] || (!world[2] && world[0]))));
            Check("SR", inputs, expected, actual.State[0], violations);
            if (actual.HasControlConflict !=
                (inputs[1] == LogicValue.One && inputs[2] == LogicValue.One))
            {
                violations.Add($"SR conflict({Format(inputs)}) was incorrect");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task JkFlipFlop_EveryFourStateInput_MatchesReachableControlWorlds()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(3))
        {
            var actual = SequentialEvaluation.JkFlipFlop(
                inputs[0], inputs[1], inputs[2]);
            var expected = Merge(BinaryWorlds(inputs).Select(world => Boolean(
                (world[1], world[2]) switch
                {
                    (false, false) => world[0],
                    (true, false) => true,
                    (false, true) => false,
                    (true, true) => !world[0],
                })));
            Check("JK", inputs, expected, actual[0], violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task TFlipFlop_EveryFourStateInput_MatchesReachableControlWorlds()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(2))
        {
            var actual = SequentialEvaluation.TFlipFlop(inputs[0], inputs[1]);
            var expected = Merge(BinaryWorlds(inputs).Select(world =>
                Boolean(world[1] ? !world[0] : world[0])));
            Check("T", inputs, expected, actual[0], violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task EdgeClassification_EveryFourStateTransition_MatchesContract()
    {
        var violations = new List<string>();
        foreach (var values in Tuples(2))
        {
            var previous = values[0];
            var current = values[1];
            CheckBoolean(
                $"rising({Format(values)})",
                previous == LogicValue.Zero && current == LogicValue.One,
                SequentialEvaluation.IsConfiguredDefiniteEdge(previous, current, rising: true),
                violations);
            CheckBoolean(
                $"falling({Format(values)})",
                previous == LogicValue.One && current == LogicValue.Zero,
                SequentialEvaluation.IsConfiguredDefiniteEdge(previous, current, rising: false),
                violations);
            CheckBoolean(
                $"indefinite({Format(values)})",
                previous != current
                    && (previous is LogicValue.X or LogicValue.Z
                        || current is LogicValue.X or LogicValue.Z),
                SequentialEvaluation.IsIndefiniteTransition(previous, current),
                violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    private static void Check(
        string operation,
        LogicValue[] inputs,
        LogicValue expected,
        LogicValue actual,
        List<string> violations)
    {
        if (expected != actual)
        {
            violations.Add(
                $"{operation}({Format(inputs)}): expected {expected}, actual {actual}");
        }
    }

    private static void CheckBoolean(
        string scenario,
        bool expected,
        bool actual,
        List<string> violations)
    {
        if (expected != actual)
        {
            violations.Add($"{scenario}: expected {expected}, actual {actual}");
        }
    }

}
