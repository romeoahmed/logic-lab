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
            from firstGap in Gen.Choose(1, MaximumLogicalTimeGap)
            from remainingGaps in Gen.Choose(0, MaximumLogicalTimeGap).ArrayOf(count - 1)
            from values in Gen.Elements(Enum.GetValues<LogicValue>()).ArrayOf(count)
            let gaps = new[] { firstGap }.Concat(remainingGaps).ToArray()
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
            var value = index > 0 && gaps[index] == 0
                ? stimuli[index - 1].Value
                : values[index];
            stimuli[index] = new GeneratedStimulus(logicalTime, value);
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
            foreach (var logicalTime in ShrinkLogicalTime(stimulus.LogicalTime))
            {
                if (sample.InsertionOrder.Where((_, candidateIndex) =>
                        candidateIndex != index).Any(candidate =>
                        candidate.LogicalTime == logicalTime
                        && candidate.Value != stimulus.Value))
                {
                    continue;
                }

                var timeCandidate = (GeneratedStimulus[])sample.InsertionOrder.Clone();
                timeCandidate[index] = stimulus with { LogicalTime = logicalTime };
                yield return sample with { InsertionOrder = timeCandidate };
            }

            if (stimulus.Value == LogicValue.Zero)
            {
                continue;
            }

            if (sample.InsertionOrder.Take(index).Any(candidate =>
                candidate.LogicalTime == stimulus.LogicalTime))
            {
                continue;
            }

            var stimuli = (GeneratedStimulus[])sample.InsertionOrder.Clone();
            for (var candidateIndex = 0; candidateIndex < stimuli.Length; candidateIndex++)
            {
                if (stimuli[candidateIndex].LogicalTime == stimulus.LogicalTime)
                {
                    stimuli[candidateIndex] = stimuli[candidateIndex] with
                    {
                        Value = LogicValue.Zero,
                    };
                }
            }

            yield return sample with { InsertionOrder = stimuli };
        }
    }

    private static IEnumerable<ulong> ShrinkLogicalTime(ulong logicalTime)
    {
        if (logicalTime <= 1)
        {
            yield break;
        }

        yield return 1;
        var half = logicalTime / 2;
        if (half > 1)
        {
            yield return half;
        }
    }
}
