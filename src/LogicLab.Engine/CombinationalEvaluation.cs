using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine;

internal readonly record struct PriorityEncoderResult(
    LogicVector Index,
    LogicValue Valid);

internal static class CombinationalEvaluation
{
    public static LogicVector Gate(
        SimulationEvaluatorKind kind,
        IReadOnlyList<LogicVector> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count < 2 || inputs.Any(input => input is null))
        {
            throw new ArgumentException("A gate requires at least two inputs.", nameof(inputs));
        }

        var operation = kind switch
        {
            SimulationEvaluatorKind.LogicAnd or SimulationEvaluatorKind.LogicNand =>
                (Func<LogicVector, LogicVector, LogicVector>)VectorLogic.And,
            SimulationEvaluatorKind.LogicOr or SimulationEvaluatorKind.LogicNor =>
                VectorLogic.Or,
            SimulationEvaluatorKind.LogicXor or SimulationEvaluatorKind.LogicXnor =>
                VectorLogic.Xor,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The evaluator is not a gate family."),
        };
        var result = inputs.Skip(1).Aggregate(
            VectorLogic.NormalizeInput(inputs[0]),
            operation);
        return kind is SimulationEvaluatorKind.LogicNand
            or SimulationEvaluatorKind.LogicNor
            or SimulationEvaluatorKind.LogicXnor
            ? VectorLogic.Not(result)
            : result;
    }

    public static LogicVector TriState(
        LogicVector data,
        LogicValue enable,
        bool activeHigh)
    {
        ArgumentNullException.ThrowIfNull(data);
        var normalizedEnable = ScalarLogic.NormalizeInput(enable);
        var isActive = activeHigh ? normalizedEnable : ScalarLogic.Not(normalizedEnable);
        var enabled = VectorLogic.NormalizeInput(data);
        var disabled = Uniform(data.Width, LogicValue.Z);
        return isActive switch
        {
            LogicValue.One => enabled,
            LogicValue.Zero => disabled,
            LogicValue.X => VectorConservativeMerge.Merge([enabled, disabled]),
            _ => throw new InvalidOperationException("Enable normalization failed."),
        };
    }

    public static LogicVector Mux(
        IReadOnlyList<LogicVector> dataInputs,
        LogicVector selector)
    {
        ArgumentNullException.ThrowIfNull(dataInputs);
        ArgumentNullException.ThrowIfNull(selector);
        var expectedCount = OutputCount(selector.Width);
        if (dataInputs.Count != expectedCount
            || dataInputs.Any(input => input is null))
        {
            throw new ArgumentException(
                "MUX data input count must equal two to the selector width.",
                nameof(dataInputs));
        }

        var normalizedSelector = VectorLogic.NormalizeInput(selector);
        var reachable = dataInputs
            .Where((_, index) => IsCompatibleIndex(normalizedSelector, checked((uint)index)))
            .Select(VectorLogic.NormalizeInput)
            .ToArray();
        return VectorConservativeMerge.Merge(reachable);
    }

    public static LogicVector[] Demux(LogicVector data, LogicVector selector)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(selector);
        var outputCount = OutputCount(selector.Width);
        var normalizedSelector = VectorLogic.NormalizeInput(selector);
        var normalizedData = VectorLogic.NormalizeInput(data);
        var zero = Uniform(data.Width, LogicValue.Zero);
        var selectorIsKnown = IsKnown(normalizedSelector);
        return [.. Enumerable.Range(0, outputCount)
            .Select(index => IsCompatibleIndex(normalizedSelector, checked((uint)index))
                ? selectorIsKnown
                    ? normalizedData
                    : VectorConservativeMerge.Merge([normalizedData, zero])
                : zero)];
    }

    public static LogicVector[] Decoder(
        LogicVector address,
        LogicValue enable,
        bool activeHigh)
    {
        ArgumentNullException.ThrowIfNull(address);
        var normalizedAddress = VectorLogic.NormalizeInput(address);
        var normalizedEnable = ScalarLogic.NormalizeInput(enable);
        var active = activeHigh ? normalizedEnable : ScalarLogic.Not(normalizedEnable);
        var outputCount = OutputCount(address.Width);
        var addressIsKnown = IsKnown(normalizedAddress);
        return [.. Enumerable.Range(0, outputCount)
            .Select(index =>
            {
                var possible = new List<LogicValue>(3);
                var addressMatchesIndex = IsCompatibleIndex(
                    normalizedAddress,
                    checked((uint)index));
                if (active is LogicValue.Zero or LogicValue.X)
                {
                    possible.Add(LogicValue.Zero);
                }

                if (active is LogicValue.One or LogicValue.X)
                {
                    if (addressMatchesIndex)
                    {
                        possible.Add(LogicValue.One);
                    }

                    if (!addressIsKnown || !addressMatchesIndex)
                    {
                        possible.Add(LogicValue.Zero);
                    }
                }

                return Uniform(1, ConservativeMerge.Merge(possible));
            })];
    }

    public static PriorityEncoderResult PriorityEncoder(
        IReadOnlyList<LogicValue> inputs,
        bool lowestIndex)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count < 2)
        {
            throw new ArgumentException(
                "A priority encoder requires at least two inputs.",
                nameof(inputs));
        }

        var normalized = inputs.Select(ScalarLogic.NormalizeInput).ToArray();
        var possibleResults = new List<(uint Index, LogicValue Valid)>();
        var candidate = lowestIndex ? 0 : inputs.Count - 1;
        var step = lowestIndex ? 1 : -1;
        var higherCanAllBeZero = true;
        for (; candidate >= 0 && candidate < inputs.Count; candidate += step)
        {
            var candidateCanBeOne = normalized[candidate] is LogicValue.One or LogicValue.X;
            if (candidateCanBeOne && higherCanAllBeZero)
            {
                possibleResults.Add((checked((uint)candidate), LogicValue.One));
            }

            higherCanAllBeZero &= normalized[candidate] is LogicValue.Zero or LogicValue.X;
        }

        if (higherCanAllBeZero)
        {
            possibleResults.Add((0, LogicValue.Zero));
        }

        var width = Math.Max(1, System.Numerics.BitOperations.Log2(
            checked((uint)inputs.Count - 1)) + 1);
        var indices = possibleResults
            .Select(result => UnsignedVector(result.Index, width))
            .ToArray();
        return new PriorityEncoderResult(
            VectorConservativeMerge.Merge(indices),
            ConservativeMerge.Merge([.. possibleResults.Select(result => result.Valid)]));
    }

    private static bool IsCompatibleIndex(LogicVector selector, uint index)
    {
        for (var bit = 0; bit < selector.Width; bit++)
        {
            var expected = ((index >> bit) & 1U) == 0 ? LogicValue.Zero : LogicValue.One;
            if (selector[bit] != LogicValue.X && selector[bit] != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnown(LogicVector value)
    {
        return Enumerable.Range(0, value.Width).All(index => value[index] != LogicValue.X);
    }

    private static LogicVector Uniform(int width, LogicValue value)
    {
        return new LogicVector([.. Enumerable.Repeat(value, width)]);
    }

    private static int OutputCount(int selectorWidth)
    {
        if (selectorWidth >= 31)
        {
            throw new OverflowException(
                "The selector shape exceeds the addressable collection size.");
        }

        return checked(1 << selectorWidth);
    }

    private static LogicVector UnsignedVector(uint value, int width)
    {
        return new LogicVector(
            [.. Enumerable.Range(0, width).Select(bit =>
                ((value >> bit) & 1U) == 0
                    ? LogicValue.Zero
                    : LogicValue.One)]);
    }
}
