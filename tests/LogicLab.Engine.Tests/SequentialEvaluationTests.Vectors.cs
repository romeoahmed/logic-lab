using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;
using static LogicLab.Engine.Tests.FourStateTestData;

namespace LogicLab.Engine.Tests;

internal sealed partial class SequentialEvaluationTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property StorageAndEnable_PackedWidthsAndFourStateInputs_MatchScalarCaptureAndHoldModel(LogicVectorArithmeticCase sample)
    {
        static LogicValue Stored(LogicValue value) => value == LogicValue.Z ? LogicValue.X : value;
        var current = sample.Left.Select(Stored).ToArray();
        var captured = sample.Right.Select(Stored).ToArray();
        var expected = current.Select((value, bit) => sample.Control switch
        {
            LogicValue.Zero => value,
            LogicValue.One => captured[bit],
            LogicValue.X or LogicValue.Z => value == captured[bit] ? value : LogicValue.X,
            _ => throw new InvalidOperationException(),
        }).ToArray();
        var data = new LogicVector(sample.Right);
        return (LogicVectorTestData.Matches(SequentialEvaluation.NormalizeForStorage(data), captured)
            && LogicVectorTestData.Matches(SequentialEvaluation.WithEnable(new LogicVector(current), data, sample.Control), expected))
            .ToProperty().Label("storage capture and possible hold/capture cases agree with the scalar model")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

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
        var expectedTerminal = Terminal(sample.Current, sample.TowardHigh);
        var expectedSerial = sample.Current[sample.TowardHigh ? sample.Width - 1 : 0];

        return actualShift.AsSpan().SequenceEqual(expectedShift)
            .Label($"shift expected [{Format(expectedShift)}], actual [{Format(actualShift)}]")
            .And(actualCounter.AsSpan().SequenceEqual(expectedCounter)
                .Label($"counter expected [{Format(expectedCounter)}], actual [{Format(actualCounter)}]"))
            .And((terminal == expectedTerminal)
                .Label($"terminal expected {expectedTerminal}, actual {terminal}"))
            .And((serial == expectedSerial)
                .Label($"serial expected {expectedSerial}, actual {serial}"))
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
