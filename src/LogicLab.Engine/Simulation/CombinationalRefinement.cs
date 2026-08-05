using LogicLab.Domain;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal static class CombinationalRefinement
{
    public static void RequirePreservingOrRefining(
        LogicVector previous,
        LogicVector current,
        ComponentContractKey contractKey,
        CompilationSource primary,
        CompilationSource related)
    {
        if (previous.Width != current.Width)
        {
            throw new SimulationContractDefectException(
                contractKey,
                "coordinate_shape",
                primary,
                related);
        }

        for (var bit = 0; bit < previous.Width; bit++)
        {
            if (previous[bit] != LogicValue.X && previous[bit] != current[bit])
            {
                throw new SimulationContractDefectException(
                    contractKey,
                    "information_order",
                    primary,
                    related);
            }
        }
    }
}
