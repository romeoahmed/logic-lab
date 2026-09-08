namespace LogicLab.Application.Workspaces;

public sealed record WorkspaceId
{
    public WorkspaceId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }

    internal static WorkspaceId Create() => new(Guid.CreateVersion7().ToString("N"));
}
