namespace LogicLab.Domain.Components;

public enum ComponentPortCardinality
{
    Fixed,
    ParameterItems,
}

public enum ComponentPortWidthSource
{
    ParameterValue,
    SliceLength,
    WidthItem,
    WidthSum,
}

public enum ComponentPortIndexing
{
    None,
    ZeroBasedDecimal,
}

public sealed class ComponentPortSchema
{
    internal ComponentPortSchema(
        string id,
        PortDirection direction,
        string widthParameterId)
        : this(
            id,
            direction,
            ComponentPortCardinality.Fixed,
            ComponentPortIndexing.None,
            ComponentPortWidthSource.ParameterValue,
            widthParameterId)
    {
    }

    internal ComponentPortSchema(
        string id,
        PortDirection direction,
        ComponentPortCardinality cardinality,
        ComponentPortIndexing indexing,
        ComponentPortWidthSource widthSource,
        string parameterId)
    {
        if (!IsValidCombination(cardinality, indexing, widthSource))
        {
            throw new ArgumentException(
                "The Port cardinality and width source combination is undefined.",
                nameof(widthSource));
        }

        Id = id;
        Direction = direction;
        Cardinality = cardinality;
        Indexing = indexing;
        WidthSource = widthSource;
        ParameterId = parameterId;
    }

    public string Id { get; }

    public PortDirection Direction { get; }

    public ComponentPortCardinality Cardinality { get; }

    public ComponentPortIndexing Indexing { get; }

    public ComponentPortWidthSource WidthSource { get; }

    public string ParameterId { get; }

    private static bool IsValidCombination(
        ComponentPortCardinality cardinality,
        ComponentPortIndexing indexing,
        ComponentPortWidthSource widthSource)
    {
        return (cardinality, indexing, widthSource) is
            (ComponentPortCardinality.Fixed,
                ComponentPortIndexing.None,
                ComponentPortWidthSource.ParameterValue)
            or (ComponentPortCardinality.Fixed,
                ComponentPortIndexing.None,
                ComponentPortWidthSource.WidthSum)
            or (ComponentPortCardinality.ParameterItems,
                ComponentPortIndexing.ZeroBasedDecimal,
                ComponentPortWidthSource.SliceLength)
            or (ComponentPortCardinality.ParameterItems,
                ComponentPortIndexing.ZeroBasedDecimal,
                ComponentPortWidthSource.WidthItem);
    }
}
