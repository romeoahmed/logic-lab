using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;
using static LogicLab.Engine.Tests.FourStateTestData;

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

internal sealed class SequentialEvaluationProperties
{
    [Test, FsCheckProperty(
        MaxTest = 100,
        Arbitrary = new[] { typeof(SequentialEvaluationArbitraries) })]
    public Property ShiftCounterAndTerminal_ReachableControls_MatchScalarOracle(
        SequentialVectorCase sample)
    {
        var current = new LogicVector(sample.Current);
        var parallel = new LogicVector(sample.Parallel);
        var shift = SequentialEvaluation.ShiftRegister(
            current,
            parallel,
            sample.Serial,
            sample.Load,
            sample.Enable,
            sample.TowardHigh);
        var counter = SequentialEvaluation.Counter(
            current,
            parallel,
            sample.Load,
            sample.Enable,
            sample.TowardHigh);
        var expectedShift = ReachableControlResult(
            sample.Current,
            Normalize(sample.Parallel),
            Shifted(sample),
            sample.Load,
            sample.Enable);
        var expectedCounter = ReachableControlResult(
            sample.Current,
            Normalize(sample.Parallel),
            Counted(sample.Current, sample.TowardHigh),
            sample.Load,
            sample.Enable);
        var actualShift = Values(shift);
        var actualCounter = Values(counter);
        var terminal = SequentialEvaluation.CounterTerminal(
            current, sample.TowardHigh);
        var serial = SequentialEvaluation.ShiftSerialOutput(
            current, sample.TowardHigh);
        var matches = actualShift.AsSpan().SequenceEqual(expectedShift)
            && actualCounter.AsSpan().SequenceEqual(expectedCounter)
            && terminal == Terminal(sample.Current, sample.TowardHigh)
            && serial == sample.Current[sample.TowardHigh ? sample.Width - 1 : 0];

        return matches
            .Label(
                $"shift expected [{Format(expectedShift)}], actual [{Format(actualShift)}]; "
                + $"counter expected [{Format(expectedCounter)}], "
                + $"actual [{Format(actualCounter)}]")
            .Collect($"width={sample.Width}")
            .Classify(sample.Load is LogicValue.X or LogicValue.Z, "uncertain load")
            .Classify(sample.Enable is LogicValue.X or LogicValue.Z, "uncertain enable");
    }

    [Test]
    public async Task Counter_PackedBoundaryWidths_WrapModuloWidth()
    {
        var violations = new List<int>();
        foreach (var width in SequentialEvaluationArbitraries.BoundaryWidths)
        {
            var ones = new LogicVector(
                [.. Enumerable.Repeat(LogicValue.One, width)]);
            var zeros = LogicVector.CreateFilled(width, LogicValue.Zero);
            var up = SequentialEvaluation.Counter(
                ones, zeros, LogicValue.Zero, LogicValue.One, countUp: true);
            var down = SequentialEvaluation.Counter(
                zeros, zeros, LogicValue.Zero, LogicValue.One, countUp: false);
            if (Values(up).Any(value => value != LogicValue.Zero)
                || Values(down).Any(value => value != LogicValue.One))
            {
                violations.Add(width);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static LogicValue[] ReachableControlResult(
        LogicValue[] current,
        LogicValue[] parallel,
        LogicValue[] activeResult,
        LogicValue load,
        LogicValue enable)
    {
        var candidates = new List<LogicValue[]>(4);
        foreach (var loadActive in ControlWorlds(load))
        {
            foreach (var enableActive in ControlWorlds(enable))
            {
                candidates.Add(loadActive
                    ? parallel
                    : enableActive ? activeResult : current);
            }
        }

        var result = new LogicValue[current.Length];
        for (var bit = 0; bit < result.Length; bit++)
        {
            result[bit] = Merge(candidates.Select(candidate => candidate[bit]));
        }

        return result;
    }

    private static LogicValue[] Shifted(SequentialVectorCase sample)
    {
        var result = new LogicValue[sample.Width];
        if (sample.TowardHigh)
        {
            result[0] = Normalize(sample.Serial);
            Array.Copy(sample.Current, 0, result, 1, sample.Width - 1);
        }
        else
        {
            Array.Copy(sample.Current, 1, result, 0, sample.Width - 1);
            result[^1] = Normalize(sample.Serial);
        }

        return result;
    }

    private static LogicValue[] Counted(LogicValue[] current, bool countUp)
    {
        var result = new LogicValue[current.Length];
        var carryOrBorrow = new[] { true };
        for (var bit = 0; bit < result.Length; bit++)
        {
            var possibleBits = PossibleBits(current[bit]);
            var output = new List<LogicValue>(4);
            var next = new HashSet<bool>();
            foreach (var value in possibleBits)
            {
                foreach (var carry in carryOrBorrow)
                {
                    output.Add(Boolean(value ^ carry));
                    next.Add(countUp ? value && carry : !value && carry);
                }
            }

            result[bit] = Merge(output);
            carryOrBorrow = [.. next];
        }

        return result;
    }

    private static LogicValue Terminal(LogicValue[] current, bool countUp)
    {
        var terminal = countUp ? LogicValue.One : LogicValue.Zero;
        if (current.Any(value => value != LogicValue.X && value != terminal))
        {
            return LogicValue.Zero;
        }

        return current.Contains(LogicValue.X) ? LogicValue.X : LogicValue.One;
    }

    private static bool[] PossibleBits(LogicValue value) => value switch
    {
        LogicValue.Zero => [false],
        LogicValue.One => [true],
        LogicValue.X => [false, true],
        _ => throw new InvalidOperationException("Stored state cannot be high impedance."),
    };

    private static bool[] ControlWorlds(LogicValue value) => value switch
    {
        LogicValue.Zero => [false],
        LogicValue.One => [true],
        LogicValue.X or LogicValue.Z => [false, true],
        _ => throw new InvalidOperationException("The control value is undefined."),
    };

    private static LogicValue[] Normalize(LogicValue[] values) =>
        [.. values.Select(Normalize)];

    private static LogicValue Normalize(LogicValue value) =>
        value == LogicValue.Z ? LogicValue.X : value;

    private static LogicValue[] Values(LogicVector vector) =>
        [.. Enumerable.Range(0, vector.Width).Select(bit => vector[bit])];
}
