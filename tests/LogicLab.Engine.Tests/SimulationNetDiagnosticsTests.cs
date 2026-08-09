using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal readonly record struct ClockTransition(
    LogicValue Previous,
    LogicValue Current);

internal sealed record SimulationDiagnosticCanonicalizationCase(
    ClockTransition[] Transitions)
{
    public override string ToString() =>
        $"Diagnostics({string.Join(", ", Transitions)})";
}

internal static class SimulationNetDiagnosticArbitraries
{
    private const int MaximumDiagnosticCount = 24;

    public static Arbitrary<SimulationDiagnosticCanonicalizationCase> Diagnostics()
    {
        var logicValue = Gen.Elements(Enum.GetValues<LogicValue>());
        var transition =
            from previous in logicValue
            from current in logicValue
            select new ClockTransition(previous, current);
        var generator =
            from count in Gen.Choose(0, MaximumDiagnosticCount)
            from transitions in transition.ArrayOf(count)
            select new SimulationDiagnosticCanonicalizationCase(transitions);

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<SimulationDiagnosticCanonicalizationCase> Shrink(
        SimulationDiagnosticCanonicalizationCase sample)
    {
        for (var index = 0; index < sample.Transitions.Length; index++)
        {
            yield return sample with
            {
                Transitions =
                [.. sample.Transitions.Where(
                    (_, candidateIndex) => candidateIndex != index)],
            };

            var transition = sample.Transitions[index];
            if (transition.Previous != LogicValue.Zero)
            {
                yield return Replace(
                    sample,
                    index,
                    transition with { Previous = LogicValue.Zero });
            }

            if (transition.Current != LogicValue.Zero)
            {
                yield return Replace(
                    sample,
                    index,
                    transition with { Current = LogicValue.Zero });
            }
        }
    }

    private static SimulationDiagnosticCanonicalizationCase Replace(
        SimulationDiagnosticCanonicalizationCase sample,
        int index,
        ClockTransition transition)
    {
        var transitions = (ClockTransition[])sample.Transitions.Clone();
        transitions[index] = transition;
        return sample with { Transitions = transitions };
    }
}

internal sealed class SimulationNetDiagnosticsTests
{
    [Test, FsCheckProperty(
        Arbitrary = new[] { typeof(SimulationNetDiagnosticArbitraries) })]
    public Property Canonicalize_PermutedDuplicateArguments_MatchesDistinctOrderedModel(
        SimulationDiagnosticCanonicalizationCase sample)
    {
        var canonical = SimulationNetDiagnostics.Canonicalize(
            sample.Transitions.Select(IndefiniteClockDiagnostic));
        var actual = canonical.Select(Transition).ToArray();
        var expected = sample.Transitions
            .Distinct()
            .OrderBy(static transition => transition.Previous)
            .ThenBy(static transition => transition.Current)
            .ToArray();
        var recanonicalized = SimulationNetDiagnostics.Canonicalize(canonical)
            .Select(Transition)
            .ToArray();

        return (actual.SequenceEqual(expected)
                && recanonicalized.SequenceEqual(actual))
            .Label("canonicalization matches the distinct ordered model and is idempotent")
            .Collect($"input={sample.Transitions.Length}")
            .Collect($"distinct={expected.Length}");
    }

    private static SimulationDiagnostic IndefiniteClockDiagnostic(
        ClockTransition transition)
    {
        return new SimulationDiagnostic(
            "simulation_indefinite_clock_edge",
            SimulationDiagnosticSeverity.Warning,
            [
                new SimulationDiagnosticArgument(
                    "previous",
                    new SimulationLogicValue(transition.Previous)),
                new SimulationDiagnosticArgument(
                    "current",
                    new SimulationLogicValue(transition.Current)),
            ],
            primary: null,
            []);
    }

    private static ClockTransition Transition(SimulationDiagnostic diagnostic)
    {
        return new ClockTransition(
            ((SimulationLogicValue)diagnostic.Arguments[0].Value).Value,
            ((SimulationLogicValue)diagnostic.Arguments[1].Value).Value);
    }
}
