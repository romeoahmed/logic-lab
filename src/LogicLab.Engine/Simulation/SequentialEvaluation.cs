using LogicLab.Domain;

namespace LogicLab.Engine.Simulation;

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
        var normalizedData = NormalizeForStorage(data);
        return enable switch
        {
            LogicValue.Zero => current,
            LogicValue.One => normalizedData,
            LogicValue.X or LogicValue.Z => VectorConservativeMerge.Merge(
                [current, normalizedData]),
            _ => throw new InvalidOperationException(
                "The sequential enable value is undefined."),
        };
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
}
