namespace LogicLab.Domain.Components;

public sealed class ComponentPortShape
{
    internal ComponentPortShape(
        ulong portCount,
        ulong inputPortCount,
        ulong outputPortCount)
    {
        PortCount = portCount;
        InputPortCount = inputPortCount;
        OutputPortCount = outputPortCount;
    }

    public ulong PortCount { get; }

    public ulong InputPortCount { get; }

    public ulong OutputPortCount { get; }

    internal static ComponentPortShape Empty { get; } = new(0, 0, 0);
}
