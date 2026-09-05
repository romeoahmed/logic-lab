using LogicLab.Domain;

namespace LogicLab.Engine.Simulation;

internal readonly record struct SrLatchEvaluation(
    LogicVector State,
    bool HasControlConflict);

internal static class SequentialEvaluation
{
    public static LogicVector NormalizeForStorage(LogicVector value)
    {
        return VectorLogic.NormalizeInput(value);
    }

    public static LogicVector WithEnable(
        LogicVector current,
        LogicVector data,
        LogicValue enable)
    {
        return enable switch
        {
            LogicValue.Zero => current,
            LogicValue.One => NormalizeForStorage(data),
            LogicValue.X or LogicValue.Z => VectorConservativeMerge.Merge(
                [current, NormalizeForStorage(data)]),
            _ => throw new InvalidOperationException(
                "The sequential enable value is undefined."),
        };
    }

    public static SrLatchEvaluation SrLatch(
        LogicValue current,
        LogicValue set,
        LogicValue reset)
    {
        var normalizedCurrent = ScalarLogic.NormalizeInput(current);
        var normalizedSet = ScalarLogic.NormalizeInput(set);
        var normalizedReset = ScalarLogic.NormalizeInput(reset);
        var candidates = new List<LogicValue>(4);
        foreach (var setActive in ReachableControlValues(normalizedSet))
        {
            foreach (var resetActive in ReachableControlValues(normalizedReset))
            {
                candidates.Add(
                    (setActive, resetActive) switch
                    {
                        (false, false) => normalizedCurrent,
                        (true, false) => LogicValue.One,
                        (false, true) => LogicValue.Zero,
                        (true, true) => LogicValue.X,
                    });
            }
        }

        return new SrLatchEvaluation(
            new LogicVector([ConservativeMerge.Merge(candidates)]),
            normalizedSet == LogicValue.One && normalizedReset == LogicValue.One);
    }

    public static LogicVector JkFlipFlop(
        LogicValue current,
        LogicValue j,
        LogicValue k)
    {
        var normalizedCurrent = ScalarLogic.NormalizeInput(current);
        var candidates = new List<LogicValue>(4);
        foreach (var jActive in ReachableControlValues(ScalarLogic.NormalizeInput(j)))
        {
            foreach (var kActive in ReachableControlValues(ScalarLogic.NormalizeInput(k)))
            {
                candidates.Add(
                    (jActive, kActive) switch
                    {
                        (false, false) => normalizedCurrent,
                        (true, false) => LogicValue.One,
                        (false, true) => LogicValue.Zero,
                        (true, true) => ScalarLogic.Not(normalizedCurrent),
                    });
            }
        }

        return new LogicVector([ConservativeMerge.Merge(candidates)]);
    }

    public static LogicVector TFlipFlop(LogicValue current, LogicValue toggle)
    {
        return new LogicVector([ScalarLogic.Xor(current, toggle)]);
    }

    public static LogicVector ShiftRegister(
        LogicVector current,
        LogicVector parallel,
        LogicValue serial,
        LogicValue load,
        LogicValue enable,
        bool towardHigh)
    {
        if (current.Width != parallel.Width)
        {
            throw new ArgumentException(
                "Sequential shift data must match the state width.",
                nameof(parallel));
        }

        var shifted = new LogicValue[current.Width];
        var normalizedSerial = ScalarLogic.NormalizeInput(serial);
        if (towardHigh)
        {
            shifted[0] = normalizedSerial;
            for (var bit = 1; bit < shifted.Length; bit++)
            {
                shifted[bit] = ScalarLogic.NormalizeInput(current[bit - 1]);
            }
        }
        else
        {
            shifted[^1] = normalizedSerial;
            for (var bit = 0; bit < shifted.Length - 1; bit++)
            {
                shifted[bit] = ScalarLogic.NormalizeInput(current[bit + 1]);
            }
        }

        var shiftedOrHeld = WithEnable(current, new LogicVector(shifted), enable);
        return WithEnable(shiftedOrHeld, parallel, load);
    }

    public static LogicValue ShiftSerialOutput(LogicVector current, bool towardHigh)
    {
        return ScalarLogic.NormalizeInput(
            current[towardHigh ? current.Width - 1 : 0]);
    }

    public static LogicVector Counter(
        LogicVector current,
        LogicVector loadValue,
        LogicValue load,
        LogicValue enable,
        bool countUp)
    {
        if (current.Width != loadValue.Width)
        {
            throw new ArgumentException(
                "Counter load data must match the state width.",
                nameof(loadValue));
        }

        var one = new LogicValue[current.Width];
        one[0] = LogicValue.One;
        var unit = new LogicVector(one);
        var counted = countUp
            ? ArithmeticEvaluation.Add(current, unit, LogicValue.Zero).Sum
            : ArithmeticEvaluation.Subtract(current, unit, LogicValue.Zero).Difference;
        var countedOrHeld = WithEnable(current, counted, enable);
        return WithEnable(countedOrHeld, loadValue, load);
    }

    public static LogicValue CounterTerminal(LogicVector current, bool countUp)
    {
        var terminalBit = countUp ? LogicValue.One : LogicValue.Zero;
        var hasUnknown = false;
        for (var bit = 0; bit < current.Width; bit++)
        {
            var value = ScalarLogic.NormalizeInput(current[bit]);
            if (value == LogicValue.X)
            {
                hasUnknown = true;
            }
            else if (value != terminalBit)
            {
                return LogicValue.Zero;
            }
        }

        return hasUnknown ? LogicValue.X : LogicValue.One;
    }

    public static bool IsConfiguredDefiniteEdge(
        LogicValue previous,
        LogicValue current,
        bool rising)
    {
        return rising
            ? previous == LogicValue.Zero && current == LogicValue.One
            : previous == LogicValue.One && current == LogicValue.Zero;
    }

    public static bool IsIndefiniteTransition(
        LogicValue previous,
        LogicValue current)
    {
        return previous != current
            && (previous is LogicValue.X or LogicValue.Z
                || current is LogicValue.X or LogicValue.Z);
    }

    private static ReadOnlySpan<bool> ReachableControlValues(LogicValue value)
    {
        return value switch
        {
            LogicValue.Zero => [false],
            LogicValue.One => [true],
            LogicValue.X => [false, true],
            _ => throw new InvalidOperationException(
                "The normalized sequential control value is undefined."),
        };
    }
}
