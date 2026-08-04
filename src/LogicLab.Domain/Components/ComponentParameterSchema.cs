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
        string? greaterThanParameterId = null,
        uint minimumValue = 1,
        string? memoryImageWidthParameterId = null,
        string? memoryImageAddressWidthParameterId = null)
    {
        if (minimumValue == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumValue),
                "A positive-width parameter minimum must be positive.");
        }

        var hasAnyMemoryImageShape = memoryImageWidthParameterId is not null
            || memoryImageAddressWidthParameterId is not null;
        var hasCompleteMemoryImageShape = memoryImageWidthParameterId is not null
            && memoryImageAddressWidthParameterId is not null;
        if (kind == ComponentParameterKind.MemoryImage
                ? !hasCompleteMemoryImageShape
                : hasAnyMemoryImageShape)
        {
            throw new ArgumentException(
                "A Memory Image parameter must declare both shape parameter IDs, and no other kind may declare them.");
        }

        Id = id;
        Kind = kind;
        WidthParameterId = widthParameterId;
        AllowedValues = Array.AsReadOnly(
            allowedValues is null ? [] : (string[])allowedValues.Clone());
        MinimumItemCount = minimumItemCount;
        GreaterThanParameterId = greaterThanParameterId;
        MinimumValue = minimumValue;
        MemoryImageWidthParameterId = memoryImageWidthParameterId;
        MemoryImageAddressWidthParameterId = memoryImageAddressWidthParameterId;
    }

    public string Id { get; }

    public ComponentParameterKind Kind { get; }

    public string? WidthParameterId { get; }

    public ReadOnlyCollection<string> AllowedValues { get; }

    public int MinimumItemCount { get; }

    public string? GreaterThanParameterId { get; }

    public uint MinimumValue { get; }

    public string? MemoryImageWidthParameterId { get; }

    public string? MemoryImageAddressWidthParameterId { get; }
}
