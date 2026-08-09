using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

internal readonly record struct GeneratedStimulus(
    ulong LogicalTime,
    LogicValue Value);

internal sealed record ScheduledStimuliCase(GeneratedStimulus[] InsertionOrder)
{
    public override string ToString() =>
        $"Stimuli({string.Join(", ", InsertionOrder)})";
}

internal static class SimulationRuntimeArbitraries
{
    private const int MaximumStimulusCount = 8;
    private const int MaximumLogicalTimeGap = 20;

    public static Arbitrary<ScheduledStimuliCase> ScheduledStimuli()
    {
        var generator =
            from count in Gen.Choose(1, MaximumStimulusCount)
            from gaps in Gen.Choose(1, MaximumLogicalTimeGap).ArrayOf(count)
            from values in Gen.Elements(Enum.GetValues<LogicValue>()).ArrayOf(count)
            let chronological = Chronological(gaps, values)
            from insertionOrder in Gen.Shuffle(chronological)
            select new ScheduledStimuliCase(insertionOrder);

        return Arb.From(generator, Shrink);
    }

    private static GeneratedStimulus[] Chronological(
        int[] gaps,
        LogicValue[] values)
    {
        var logicalTime = 0UL;
        var stimuli = new GeneratedStimulus[gaps.Length];
        for (var index = 0; index < gaps.Length; index++)
        {
            logicalTime = checked(logicalTime + (ulong)gaps[index]);
            stimuli[index] = new GeneratedStimulus(logicalTime, values[index]);
        }

        return stimuli;
    }

    private static IEnumerable<ScheduledStimuliCase> Shrink(
        ScheduledStimuliCase sample)
    {
        if (sample.InsertionOrder.Length > 1)
        {
            for (var index = 0; index < sample.InsertionOrder.Length; index++)
            {
                yield return sample with
                {
                    InsertionOrder =
                    [.. sample.InsertionOrder.Where(
                        (_, candidateIndex) => candidateIndex != index)],
                };
            }
        }

        for (var index = 0; index < sample.InsertionOrder.Length; index++)
        {
            var stimulus = sample.InsertionOrder[index];
            if (stimulus.Value == LogicValue.Zero)
            {
                continue;
            }

            var stimuli = (GeneratedStimulus[])sample.InsertionOrder.Clone();
            stimuli[index] = stimulus with { Value = LogicValue.Zero };
            yield return sample with { InsertionOrder = stimuli };
        }
    }
}
