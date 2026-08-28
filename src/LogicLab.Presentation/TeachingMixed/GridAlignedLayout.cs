namespace LogicLab.Presentation.TeachingMixed;

internal static class GridAlignedLayout
{
    public static int AlignUp(int value, int gridStep)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridStep);

        return checked((int)(((long)value + gridStep - 1) / gridStep * gridStep));
    }
}
