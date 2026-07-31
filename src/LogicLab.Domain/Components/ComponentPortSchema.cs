namespace LogicLab.Domain.Components;

public sealed class ComponentPortSchema
{
    internal ComponentPortSchema(
        string id,
        PortDirection direction,
        string widthParameterId)
    {
        Id = id;
        Direction = direction;
        WidthParameterId = widthParameterId;
    }

    public string Id { get; }

    public PortDirection Direction { get; }

    public string WidthParameterId { get; }
}
