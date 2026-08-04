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

        return diagnostics.ToArray();
    }

    private static string? GetInvalidValueRule(
        ComponentParameterSchema schema,
        ComponentParameterValue? value,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        CancellationToken cancellationToken)
    {
        return (schema.Kind, value) switch
        {
            (ComponentParameterKind.PositiveWidth,
                Unsigned32ParameterValue { Value: > 0 } width)
                when width.Value >= schema.MinimumValue =>
                GetInvalidWidthRule(schema, width, allParameters),
            (ComponentParameterKind.PositiveWidth, Unsigned32ParameterValue) =>
                schema.MinimumValue > 1 ? "minimumValue" : "positiveWidth",
            (ComponentParameterKind.Choice, ChoiceParameterValue choice) =>
                schema.AllowedValues.Contains(choice.Value, StringComparer.Ordinal)
                    ? null
                    : "allowedValue",
            (ComponentParameterKind.LogicVector, LogicVectorParameterValue vector) =>
                GetInvalidLogicVectorRule(
                    schema,
                    vector,
                    allParameters,
                    cancellationToken),
            (ComponentParameterKind.Slices, SlicesParameterValue slices) =>
                GetInvalidSlicesRule(
                    schema,
                    slices,
                    allParameters,
                    cancellationToken),
            (ComponentParameterKind.Widths, WidthsParameterValue widths) =>
                GetInvalidWidthsRule(schema, widths, cancellationToken),
            (ComponentParameterKind.MemoryImage, MemoryImageParameterValue) => null,
            _ => "parameterKind",
        };
    }

    private static string? GetInvalidWidthRule(
        ComponentParameterSchema schema,
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
        ComponentParameterSchema schema,
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

        var width = FindUnsignedWidth(allParameters, schema.WidthParameterId);

        return width == 0 || vector.Values.Count != width
            ? "vectorWidth"
            : null;
    }

    private static string? GetInvalidSlicesRule(
        ComponentParameterSchema schema,
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

            uint end;
            try
            {
                end = checked(slice.Offset + slice.Length);
            }
            catch (OverflowException)
            {
                return "sliceContainment";
            }

            if (width == 0 || end > width)
            {
                return "sliceContainment";
            }
        }

        return null;
    }

    private static string? GetInvalidWidthsRule(
        ComponentParameterSchema schema,
        WidthsParameterValue widths,
        CancellationToken cancellationToken)
    {
        if (widths.Values.Count < schema.MinimumItemCount)
        {
            return "minimumItemCount";
        }

        uint sum = 0;
        foreach (var width in widths.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (width == 0)
            {
                return "positiveWidth";
            }

            try
            {
                sum = checked(sum + width);
            }
            catch (OverflowException)
            {
                return "widthSum";
            }
        }

        return sum > 0 ? null : "widthSum";
    }

    private static uint FindUnsignedWidth(
        IEnumerable<ComponentParameterBinding> parameters,
        string? parameterId)
    {
        return parameters
            .Where(binding => string.Equals(
                binding.ParameterId,
                parameterId,
                StringComparison.Ordinal))
            .Select(binding => binding.Value)
            .OfType<Unsigned32ParameterValue>()
            .Select(value => value.Value)
            .FirstOrDefault();
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
            && IsAsciiLetterOrDigit(value[0])
            && value.All(IsStableTokenCharacter);
    }

    private static bool IsStableTokenCharacter(char value)
    {
        return IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
    }
}
