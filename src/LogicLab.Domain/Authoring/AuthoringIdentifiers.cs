namespace LogicLab.Domain.Authoring;

public sealed record ProjectId
{
    internal ProjectId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static ProjectId Create()
    {
        return new ProjectId(Guid.CreateVersion7().ToString("N"));
    }
}

public sealed record ProjectRevisionId
{
    internal ProjectRevisionId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static ProjectRevisionId Create()
    {
        return new ProjectRevisionId(Guid.CreateVersion7().ToString("N"));
    }
}

public sealed record CircuitDefinitionId
{
    internal CircuitDefinitionId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static CircuitDefinitionId Create()
    {
        return new CircuitDefinitionId(Guid.CreateVersion7().ToString("N"));
    }
}

public sealed record ComponentInstanceId
{
    internal ComponentInstanceId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static ComponentInstanceId Create()
    {
        return new ComponentInstanceId(Guid.CreateVersion7().ToString("N"));
    }
}

public sealed record NetId
{
    internal NetId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static NetId Create()
    {
        return new NetId(Guid.CreateVersion7().ToString("N"));
    }
}
