using LogicLab.Domain;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal static class CombinationalRefinement
{
    private const string CoordinateShapeRule = "coordinate_shape";
    private const string InformationOrderRule = "information_order";

    public static void RequireComponentOutputPreservingOrRefining(
        LogicVector previous,
        LogicVector current,
        ComponentContractKey contractKey,
        CompilationSource primary,
        CompilationSource related)
    {
        var rule = FindDefectRule(previous, current);
        if (rule is not null)
        {
            throw new SimulationContractDefectException(
                contractKey,
                rule,
                primary,
                related);
        }
    }

    public static void RequireNetResolutionPreservingOrRefining(
        LogicVector previous,
        LogicVector current)
    {
        var rule = FindDefectRule(previous, current);
        if (rule is not null)
        {
            throw new InvalidOperationException(
                $"A Runtime-owned Net resolution equation violated the {rule} invariant.");
        }
    }

    private static string? FindDefectRule(LogicVector previous, LogicVector current)
    {
        if (previous.Width != current.Width)
        {
            return CoordinateShapeRule;
        }

        for (var bit = 0; bit < previous.Width; bit++)
        {
            if (previous[bit] != LogicValue.X && previous[bit] != current[bit])
            {
                return InformationOrderRule;
            }
        }

        return null;
    }
}
