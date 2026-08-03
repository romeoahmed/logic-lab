using System.Collections.ObjectModel;

namespace LogicLab.Domain.Components;

public sealed class ComponentParameterSchema
{
    internal ComponentParameterSchema(
        string id,
        ComponentParameterKind kind,
        string? widthParameterId = null,
        string[]? allowedValues = null,
        int minimumItemCount = 0,
        string? greaterThanParameterId = null)
    {
        Id = id;
        Kind = kind;
        WidthParameterId = widthParameterId;
        AllowedValues = Array.AsReadOnly(
            allowedValues is null ? [] : (string[])allowedValues.Clone());
        MinimumItemCount = minimumItemCount;
        GreaterThanParameterId = greaterThanParameterId;
    }

    public string Id { get; }

    public ComponentParameterKind Kind { get; }

    public string? WidthParameterId { get; }

    public ReadOnlyCollection<string> AllowedValues { get; }

    public int MinimumItemCount { get; }

    public string? GreaterThanParameterId { get; }
}
