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
        if (inputs.Count < 2 || ContainsNull(inputs))
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
        var result = VectorLogic.NormalizeInput(inputs[0]);
        for (var index = 1; index < inputs.Count; index++)
        {
            result = operation(result, inputs[index]);
        }

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
            || ContainsNull(dataInputs))
        {
            throw new ArgumentException(
                "MUX data input count must equal two to the selector width.",
                nameof(dataInputs));
        }

        var normalizedSelector = VectorLogic.NormalizeInput(selector);
        var reachable = new List<LogicVector>(dataInputs.Count);
        for (var index = 0; index < dataInputs.Count; index++)
        {
            if (IsCompatibleIndex(normalizedSelector, checked((uint)index)))
            {
                reachable.Add(VectorLogic.NormalizeInput(dataInputs[index]));
            }
        }

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
        var selectedData = selectorIsKnown
            ? normalizedData
            : VectorConservativeMerge.Merge([normalizedData, zero]);
        var outputs = new LogicVector[outputCount];
        for (var index = 0; index < outputs.Length; index++)
        {
            outputs[index] = IsCompatibleIndex(normalizedSelector, checked((uint)index))
                ? selectedData
                : zero;
        }

        return outputs;
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
        var outputs = new LogicVector[outputCount];
        for (var index = 0; index < outputs.Length; index++)
        {
            var addressMatchesIndex = IsCompatibleIndex(
                normalizedAddress,
                checked((uint)index));
            var output = (active, addressMatchesIndex, addressIsKnown) switch
            {
                (_, false, _) or (LogicValue.Zero, _, _) => LogicValue.Zero,
                (LogicValue.One, true, true) => LogicValue.One,
                _ => LogicValue.X,
            };
            outputs[index] = Uniform(1, output);
        }

        return outputs;
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

        var normalized = new LogicValue[inputs.Count];
        for (var index = 0; index < normalized.Length; index++)
        {
            normalized[index] = ScalarLogic.NormalizeInput(inputs[index]);
        }

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
        var indices = new LogicVector[possibleResults.Count];
        var validValues = new LogicValue[possibleResults.Count];
        for (var index = 0; index < possibleResults.Count; index++)
        {
            var result = possibleResults[index];
            indices[index] = UnsignedVector(result.Index, width);
            validValues[index] = result.Valid;
        }

        return new PriorityEncoderResult(
            VectorConservativeMerge.Merge(indices),
            ConservativeMerge.Merge(validValues));
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
        for (var index = 0; index < value.Width; index++)
        {
            if (value[index] == LogicValue.X)
            {
                return false;
            }
        }

        return true;
    }

    private static LogicVector Uniform(int width, LogicValue value)
    {
        var values = new LogicValue[width];
        Array.Fill(values, value);
        return new LogicVector(values);
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
        var values = new LogicValue[width];
        for (var bit = 0; bit < values.Length; bit++)
        {
            values[bit] = ((value >> bit) & 1U) == 0
                ? LogicValue.Zero
                : LogicValue.One;
        }

        return new LogicVector(values);
    }

    private static bool ContainsNull(IReadOnlyList<LogicVector> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                return true;
            }
        }

        return false;
    }
}
