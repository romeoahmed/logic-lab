using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

internal static class ComponentParameterValidator
{
    public static AuthoringDiagnostic[] Validate(
        ComponentContractKey contractKey,
        ComponentContractSchema schema,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        CancellationToken cancellationToken = default)
    {
        return ValidateCore(
            contractKey,
            schema,
            parameters,
            memoryImages: null,
            cancellationToken);
    }

    public static AuthoringDiagnostic[] ValidateForDocument(
        ComponentContractKey contractKey,
        ComponentContractSchema schema,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ValidateCore(
            contractKey,
            schema,
            parameters,
            new MemoryImageLookup(document),
            cancellationToken);
    }

    internal static AuthoringDiagnostic[] ValidateForDocument(
        ComponentContractKey contractKey,
        ComponentContractSchema schema,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        Dictionary<MemoryImageId, MemoryImage> memoryImages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memoryImages);
        return ValidateCore(
            contractKey,
            schema,
            parameters,
            new MemoryImageLookup(memoryImages),
            cancellationToken);
    }

    private static AuthoringDiagnostic[] ValidateCore(
        ComponentContractKey contractKey,
        ComponentContractSchema schema,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        MemoryImageLookup? memoryImages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<AuthoringDiagnostic>();
        var availableCount = Math.Min(schema.Parameters.Count, parameters.Count);
        for (var index = 0; index < availableCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = schema.Parameters[index];
            var actual = parameters[index];

            if (!string.Equals(expected.Id, actual.ParameterId, StringComparison.Ordinal))
            {
                diagnostics.Add(InvalidParameter(
                    contractKey,
                    expected.Id,
                    "parameterOrder"));
                continue;
            }

            var rule = GetInvalidValueRule(
                expected,
                actual.Value,
                parameters,
                memoryImages,
                cancellationToken);
            if (rule is not null)
            {
                diagnostics.Add(InvalidParameter(contractKey, expected.Id, rule));
            }
        }

        for (var index = availableCount; index < schema.Parameters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(InvalidParameter(
                contractKey,
                schema.Parameters[index].Id,
                "missingParameter"));
        }

        for (var index = schema.Parameters.Count; index < parameters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(InvalidParameter(
                contractKey,
                parameters[index].ParameterId,
                "unknownParameter"));
        }

        return [.. diagnostics];
    }

    private static string? GetInvalidValueRule(
        ComponentParameterSchema schema,
        ComponentParameterValue? value,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        MemoryImageLookup? memoryImages,
        CancellationToken cancellationToken)
    {
        return (schema, value) switch
        {
            (WidthParameterSchema expected,
                Unsigned32ParameterValue { Value: > 0 } width)
                when width.Value >= expected.MinimumValue =>
                GetInvalidWidthRule(expected, width, allParameters),
            (WidthParameterSchema expected, Unsigned32ParameterValue) =>
                expected.MinimumValue > 1 ? "minimumValue" : "positiveWidth",
            (ChoiceParameterSchema expected, ChoiceParameterValue choice) =>
                expected.AllowedValues.Contains(choice.Value, StringComparer.Ordinal)
                    ? null
                    : "allowedValue",
            (LogicVectorParameterSchema expected, LogicVectorParameterValue vector) =>
                GetInvalidLogicVectorRule(
                    expected,
                    vector,
                    allParameters,
                    cancellationToken),
            (SlicesParameterSchema expected, SlicesParameterValue slices) =>
                GetInvalidSlicesRule(
                    expected,
                    slices,
                    allParameters,
                    cancellationToken),
            (WidthsParameterSchema expected, WidthsParameterValue widths) =>
                GetInvalidWidthsRule(expected, widths, cancellationToken),
            (MemoryImageParameterSchema expected, MemoryImageParameterValue image) =>
                GetInvalidMemoryImageRule(
                    expected,
                    image,
                    allParameters,
                    memoryImages,
                    cancellationToken),
            (BinaryLogicParameterSchema,
                LogicVectorParameterValue binary) =>
                binary.Values is [LogicValue.Zero or LogicValue.One]
                    ? null
                    : "binaryLogicValue",
            (PositiveUnsigned64ParameterSchema,
                Unsigned64ParameterValue { Value: > 0 }) => null,
            (PositiveUnsigned64ParameterSchema, Unsigned64ParameterValue) =>
                "positiveUnsigned64",
            _ => "parameterKind",
        };
    }

    private static string? GetInvalidMemoryImageRule(
        MemoryImageParameterSchema schema,
        MemoryImageParameterValue reference,
        IReadOnlyList<ComponentParameterBinding> allParameters,
        MemoryImageLookup? memoryImages,
        CancellationToken cancellationToken)
    {
        if (memoryImages is null)
        {
            return null;
        }

        var image = memoryImages.Value.Find(reference.MemoryImageId);
        cancellationToken.ThrowIfCancellationRequested();
        if (image is null)
        {
            return "memoryImageReference";
        }

        var wordWidth = FindUnsignedWidth(
            allParameters,
            schema.WordWidthParameterId);
        var addressWidth = FindUnsignedWidth(
            allParameters,
            schema.AddressWidthParameterId);
        if (wordWidth == 0 || addressWidth == 0)
        {
            return null;
        }

        if (addressWidth >= 32)
        {
            return "memoryImageShape";
        }

        var expectedDepth = 1u << checked((int)addressWidth);
        return image.Width == wordWidth && image.Depth == expectedDepth
            ? null
            : "memoryImageShape";
    }

    private readonly struct MemoryImageLookup
    {
        private readonly ProjectDocument? document;
        private readonly Dictionary<MemoryImageId, MemoryImage>? index;

        public MemoryImageLookup(ProjectDocument document)
        {
            this.document = document;
            index = null;
        }

        public MemoryImageLookup(Dictionary<MemoryImageId, MemoryImage> index)
        {
            document = null;
            this.index = index;
        }

        public MemoryImage? Find(MemoryImageId id) =>
            index?.GetValueOrDefault(id) ?? document?.FindMemoryImage(id);
    }

    private static string? GetInvalidWidthRule(
        WidthParameterSchema schema,
        Unsigned32ParameterValue width,
        ReadOnlyCollection<ComponentParameterBinding> allParameters)
    {
        if (schema.GreaterThanParameterId is null)
        {
            return null;
        }

        var lowerBound = FindUnsignedWidth(
            allParameters,
            schema.GreaterThanParameterId);
        return lowerBound > 0 && width.Value > lowerBound
            ? null
            : "greaterThanInputWidth";
    }

    private static string? GetInvalidLogicVectorRule(
        LogicVectorParameterSchema schema,
        LogicVectorParameterValue vector,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        CancellationToken cancellationToken)
    {
        if (vector.Values.Count == 0)
        {
            return "logicVectorValue";
        }

        foreach (var value in vector.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value is < LogicValue.Zero or > LogicValue.X)
            {
                return "logicVectorValue";
            }
        }

        var width = schema switch
        {
            FixedLogicVectorParameterSchema fixedWidth => fixedWidth.Width,
            VariableLogicVectorParameterSchema variableWidth =>
                FindUnsignedWidth(allParameters, variableWidth.WidthParameterId),
            _ => throw new InvalidOperationException("The Logic Vector width schema is undefined."),
        };

        return width == 0 || vector.Values.Count != width
            ? "vectorWidth"
            : null;
    }

    private static string? GetInvalidSlicesRule(
        SlicesParameterSchema schema,
        SlicesParameterValue slices,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        CancellationToken cancellationToken)
    {
        if (slices.Values.Count < schema.MinimumItemCount)
        {
            return "minimumItemCount";
        }

        var width = FindUnsignedWidth(allParameters, schema.WidthParameterId);
        foreach (var slice in slices.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slice.Length == 0)
            {
                return "positiveLength";
            }

            if ((ulong)slice.Offset + slice.Length > width)
            {
                return "sliceContainment";
            }
        }

        return null;
    }

    private static string? GetInvalidWidthsRule(
        WidthsParameterSchema schema,
        WidthsParameterValue widths,
        CancellationToken cancellationToken)
    {
        if (widths.Values.Count < schema.MinimumItemCount)
        {
            return "minimumItemCount";
        }

        ulong sum = 0;
        foreach (var width in widths.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (width == 0)
            {
                return "positiveWidth";
            }

            sum += width;
            if (sum > uint.MaxValue)
            {
                return "widthSum";
            }
        }

        return sum > 0 ? null : "widthSum";
    }

    private static uint FindUnsignedWidth(
        IReadOnlyList<ComponentParameterBinding> parameters,
        string parameterId)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].ParameterId == parameterId
                && parameters[index].Value is Unsigned32ParameterValue width)
            {
                return width.Value;
            }
        }

        return 0;
    }

    private static AuthoringDiagnostic InvalidParameter(
        ComponentContractKey contractKey,
        string parameterId,
        string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_parameter",
            [
                new AuthoringDiagnosticArgument(
                    "contractKey",
                    new ContractKeyDiagnosticValue(contractKey)),
                new AuthoringDiagnosticArgument(
                    "parameterId",
                    new StableTokenDiagnosticValue(
                        IsStableToken(parameterId) ? parameterId : "invalid")),
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]);
    }

    private static bool IsStableToken(string? value)
    {
        return value is { Length: >= 1 and <= 96 }
            && char.IsAsciiLetterOrDigit(value[0])
            && value.All(IsStableTokenCharacter);
    }

    private static bool IsStableTokenCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';
    }
}
