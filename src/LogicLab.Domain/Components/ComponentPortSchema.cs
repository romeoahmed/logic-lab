namespace LogicLab.Domain.Components;

public enum ComponentPortCardinality
{
    Fixed,
    ParameterItems,
    ParameterValue,
    PowerOfTwoParameterValue,
}

public enum ComponentPortWidthSource
{
    ParameterValue,
    SliceLength,
    WidthItem,
    WidthSum,
    FixedOne,
    CeilingLog2ParameterValue,
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
            widthParameterId,
            null)
    {
    }

    internal ComponentPortSchema(
        string id,
        PortDirection direction,
        ComponentPortCardinality cardinality,
        ComponentPortIndexing indexing,
        ComponentPortWidthSource widthSource,
        string parameterId,
        string? cardinalityParameterId = null)
    {
        if (!IsValidCombination(cardinality, indexing, widthSource))
        {
            throw new ArgumentException(
                "The Port cardinality and width source combination is undefined.",
                nameof(widthSource));
        }

        var hasGeneratedCount = cardinality is ComponentPortCardinality.ParameterValue
            or ComponentPortCardinality.PowerOfTwoParameterValue;
        if (hasGeneratedCount != !string.IsNullOrEmpty(cardinalityParameterId))
        {
            throw new ArgumentException(
                "Generated Port cardinality requires exactly one count parameter.",
                nameof(cardinalityParameterId));
        }

        Id = id;
        Direction = direction;
        Cardinality = cardinality;
        Indexing = indexing;
        WidthSource = widthSource;
        ParameterId = parameterId;
        CardinalityParameterId = cardinalityParameterId;
    }

    public string Id { get; }

    public PortDirection Direction { get; }

    public ComponentPortCardinality Cardinality { get; }

    public ComponentPortIndexing Indexing { get; }

    public ComponentPortWidthSource WidthSource { get; }

    public string ParameterId { get; }

    public string? CardinalityParameterId { get; }

    private static bool IsValidCombination(
        ComponentPortCardinality cardinality,
        ComponentPortIndexing indexing,
        ComponentPortWidthSource widthSource)
    {
        return cardinality switch
        {
            ComponentPortCardinality.Fixed => indexing == ComponentPortIndexing.None
                && widthSource is ComponentPortWidthSource.ParameterValue
                    or ComponentPortWidthSource.WidthSum
                    or ComponentPortWidthSource.FixedOne
                    or ComponentPortWidthSource.CeilingLog2ParameterValue,
            ComponentPortCardinality.ParameterItems => indexing == ComponentPortIndexing.ZeroBasedDecimal
                && widthSource is ComponentPortWidthSource.SliceLength or ComponentPortWidthSource.WidthItem,
            ComponentPortCardinality.ParameterValue or ComponentPortCardinality.PowerOfTwoParameterValue =>
                indexing == ComponentPortIndexing.ZeroBasedDecimal
                && widthSource is ComponentPortWidthSource.ParameterValue or ComponentPortWidthSource.FixedOne,
            _ => false,
        };
    }
}
