using LogicLab.Domain;

namespace LogicLab.Engine.Simulation;

internal static class CombinationalRefinement
{
    public static void RequirePreservingOrRefining(
        LogicVector previous,
        LogicVector current)
    {
        if (previous.Width != current.Width)
        {
            throw new InvalidOperationException(
                "A combinational equation changed its coordinate shape.");
        }

        for (var bit = 0; bit < previous.Width; bit++)
        {
            if (previous[bit] != LogicValue.X && previous[bit] != current[bit])
            {
                throw new InvalidOperationException(
                    "A combinational equation returned an incomparable or regressive value.");
            }
        }
    }
}
