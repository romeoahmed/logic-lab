using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

internal sealed record SequentialVectorCase(
    LogicValue[] Current,
    LogicValue[] Parallel,
    LogicValue Serial,
    LogicValue Load,
    LogicValue Enable,
    bool TowardHigh)
{
    public int Width => Current.Length;

    public override string ToString() =>
        $"SequentialVector(width={Width}, load={Load}, enable={Enable})";
}

internal static class SequentialEvaluationArbitraries
{
    private static readonly int[] BoundaryWidthValues = [1, 7, 8, 9, 63, 64, 65];

    private static readonly Gen<LogicValue> StoredValue = Gen.Elements(
        LogicValue.Zero,
        LogicValue.One,
        LogicValue.X);

    private static readonly Gen<LogicValue> InputValue =
        Gen.Elements(Enum.GetValues<LogicValue>());

    public static ReadOnlySpan<int> BoundaryWidths => BoundaryWidthValues;

    public static Arbitrary<SequentialVectorCase> SequentialVectors()
    {
        var generator =
            from width in Gen.Elements(BoundaryWidthValues)
            from current in StoredValue.ArrayOf(width)
            from parallel in InputValue.ArrayOf(width)
            from serial in InputValue
            from load in InputValue
            from enable in InputValue
            from towardHigh in ArbMap.Default.GeneratorFor<bool>()
            select new SequentialVectorCase(
                current, parallel, serial, load, enable, towardHigh);

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<SequentialVectorCase> Shrink(SequentialVectorCase sample)
    {
        foreach (var width in BoundaryWidthValues.Where(width => width < sample.Width))
        {
            yield return sample with
            {
                Current = sample.Current[..width],
                Parallel = sample.Parallel[..width],
            };
        }

        foreach (var current in ShrinkValues(sample.Current))
        {
            yield return sample with { Current = current };
        }

        foreach (var parallel in ShrinkValues(sample.Parallel))
        {
            yield return sample with { Parallel = parallel };
        }

        if (sample.Serial != LogicValue.Zero)
        {
            yield return sample with { Serial = LogicValue.Zero };
        }

        if (sample.Load != LogicValue.Zero)
        {
            yield return sample with { Load = LogicValue.Zero };
        }

        if (sample.Enable != LogicValue.Zero)
        {
            yield return sample with { Enable = LogicValue.Zero };
        }

        if (sample.TowardHigh)
        {
            yield return sample with { TowardHigh = false };
        }
    }

    private static IEnumerable<LogicValue[]> ShrinkValues(LogicValue[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] == LogicValue.Zero)
            {
                continue;
            }

            var candidate = (LogicValue[])values.Clone();
            candidate[index] = LogicValue.Zero;
            yield return candidate;
        }
    }
}
