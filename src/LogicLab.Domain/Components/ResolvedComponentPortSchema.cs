namespace LogicLab.Domain.Components;

public sealed class ResolvedComponentPortSchema
{
    internal ResolvedComponentPortSchema(
        string id,
        PortDirection direction,
        uint width)
    {
        Id = id;
        Direction = direction;
        Width = width;
    }

    public string Id { get; }

    public PortDirection Direction { get; }

    public uint Width { get; }
}
