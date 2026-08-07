using System.Collections.ObjectModel;

namespace LogicLab.Domain.Components;

public sealed class ComponentParameterSchema
{
    internal ComponentParameterSchema(
        string id,
        ComponentParameterKind kind,
        string? widthParameterId = null,
        ReadOnlySpan<string> allowedValues = default,
        int minimumItemCount = 0,
        string? greaterThanParameterId = null,
        uint minimumValue = 1,
        string? memoryImageWidthParameterId = null,
        string? memoryImageAddressWidthParameterId = null,
        uint? fixedWidth = null)
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

        if (fixedWidth == 0
            || (fixedWidth is not null && kind != ComponentParameterKind.LogicVector)
            || (fixedWidth is not null && widthParameterId is not null)
            || (kind == ComponentParameterKind.LogicVector
                && fixedWidth is null
                && widthParameterId is null))
        {
            throw new ArgumentException(
                "A fixed width is positive, belongs only to a Logic Vector, and cannot be combined with a width parameter.",
                nameof(fixedWidth));
        }

        Id = id;
        Kind = kind;
        WidthParameterId = widthParameterId;
        AllowedValues = Array.AsReadOnly(allowedValues.ToArray());
        MinimumItemCount = minimumItemCount;
        GreaterThanParameterId = greaterThanParameterId;
        MinimumValue = minimumValue;
        MemoryImageWidthParameterId = memoryImageWidthParameterId;
        MemoryImageAddressWidthParameterId = memoryImageAddressWidthParameterId;
        FixedWidth = fixedWidth;
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

    public uint? FixedWidth { get; }
}
