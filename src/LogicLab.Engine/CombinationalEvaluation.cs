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
        var result = operation(inputs[0], inputs[1]);
        for (var index = 2; index < inputs.Count; index++)
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
        return isActive switch
        {
            LogicValue.One => VectorLogic.NormalizeInput(data),
            LogicValue.Zero => LogicVector.CreateFilled(data.Width, LogicValue.Z),
            // Normalized data never contains Z, so every enabled/disabled pair meets at X.
            LogicValue.X => LogicVector.CreateFilled(data.Width, LogicValue.X),
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

        var reachable = new List<LogicVector>(dataInputs.Count);
        for (var index = 0; index < dataInputs.Count; index++)
        {
            if (IsCompatibleIndex(selector, checked((uint)index)))
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
        var normalizedData = VectorLogic.NormalizeInput(data);
        var zero = LogicVector.CreateFilled(data.Width, LogicValue.Zero);
        var selectorIsKnown = selector.GetHighWord(0) == 0;
        var selectedData = selectorIsKnown
            ? normalizedData
            : VectorConservativeMerge.Merge([normalizedData, zero]);
        var outputs = new LogicVector[outputCount];
        for (var index = 0; index < outputs.Length; index++)
        {
            outputs[index] = IsCompatibleIndex(selector, checked((uint)index))
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
        var normalizedEnable = ScalarLogic.NormalizeInput(enable);
        var active = activeHigh ? normalizedEnable : ScalarLogic.Not(normalizedEnable);
        var outputCount = OutputCount(address.Width);
        var addressIsKnown = address.GetHighWord(0) == 0;
        var outputs = new LogicVector[outputCount];
        for (var index = 0; index < outputs.Length; index++)
        {
            var addressMatchesIndex = IsCompatibleIndex(
                address,
                checked((uint)index));
            var output = (active, addressMatchesIndex, addressIsKnown) switch
            {
                (_, false, _) or (LogicValue.Zero, _, _) => LogicValue.Zero,
                (LogicValue.One, true, true) => LogicValue.One,
                _ => LogicValue.X,
            };
            outputs[index] = LogicVector.CreateFilled(1, output);
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

        var anyIndexBits = 0U;
        var commonIndexBits = uint.MaxValue;
        var hasPossibleAssertion = false;
        var candidate = lowestIndex ? 0 : inputs.Count - 1;
        var step = lowestIndex ? 1 : -1;
        var higherCanAllBeZero = true;
        for (; candidate >= 0 && candidate < inputs.Count; candidate += step)
        {
            var value = ScalarLogic.NormalizeInput(inputs[candidate]);
            var candidateCanBeOne = value is LogicValue.One or LogicValue.X;
            if (candidateCanBeOne && higherCanAllBeZero)
            {
                anyIndexBits |= checked((uint)candidate);
                commonIndexBits &= checked((uint)candidate);
                hasPossibleAssertion = true;
            }

            higherCanAllBeZero &= value is LogicValue.Zero or LogicValue.X;
        }

        if (higherCanAllBeZero)
        {
            commonIndexBits = 0;
        }

        var width = Math.Max(1, System.Numerics.BitOperations.Log2(
            checked((uint)inputs.Count - 1)) + 1);
        // A bit is known only when every reachable index agrees, including zero
        // when no input need be asserted.
        return new PriorityEncoderResult(
            LogicVector.CreateFromOwnedWords(
                width,
                [anyIndexBits & commonIndexBits],
                [anyIndexBits & ~commonIndexBits]),
            hasPossibleAssertion
                ? higherCanAllBeZero ? LogicValue.X : LogicValue.One
                : LogicValue.Zero);
    }

    private static bool IsCompatibleIndex(LogicVector selector, uint index)
    {
        // OutputCount bounds selectors to one word. High bits mark both X and Z,
        // so only differences at known positions rule out a candidate index.
        return ((selector.GetLowWord(0) ^ index) & ~selector.GetHighWord(0)) == 0;
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
