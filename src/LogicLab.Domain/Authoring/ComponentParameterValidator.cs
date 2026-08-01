using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

internal static class ComponentParameterValidator
{
    public static AuthoringDiagnostic[] Validate(
        ComponentContractKey contractKey,
        ComponentContractSchema schema,
        ReadOnlyCollection<ComponentParameterBinding> parameters)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        var availableCount = Math.Min(schema.Parameters.Count, parameters.Count);
        for (var index = 0; index < availableCount; index++)
        {
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

            ValidateValue(
                contractKey,
                expected,
                actual.Value,
                parameters,
                diagnostics);
        }

        for (var index = availableCount; index < schema.Parameters.Count; index++)
        {
            diagnostics.Add(InvalidParameter(
                contractKey,
                schema.Parameters[index].Id,
                "missingParameter"));
        }

        for (var index = schema.Parameters.Count; index < parameters.Count; index++)
        {
            diagnostics.Add(InvalidParameter(
                contractKey,
                parameters[index].ParameterId,
                "unknownParameter"));
        }

        return diagnostics.ToArray();
    }

    public static bool TryGetPortWidth(
        ComponentInstance instance,
        ComponentPortSchema port,
        out uint width)
    {
        width = instance.Parameters
            .Where(binding => string.Equals(
                binding.ParameterId,
                port.WidthParameterId,
                StringComparison.Ordinal))
            .Select(binding => binding.Value)
            .OfType<Unsigned32ParameterValue>()
            .Select(parameter => parameter.Value)
            .SingleOrDefault();
        return width > 0;
    }

    private static void ValidateValue(
        ComponentContractKey contractKey,
        ComponentParameterSchema schema,
        ComponentParameterValue? value,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        List<AuthoringDiagnostic> diagnostics)
    {
        switch (schema.Kind, value)
        {
            case (ComponentParameterKind.PositiveWidth, Unsigned32ParameterValue { Value: > 0 }):
                return;
            case (ComponentParameterKind.PositiveWidth, Unsigned32ParameterValue):
                diagnostics.Add(InvalidParameter(contractKey, schema.Id, "positiveWidth"));
                return;
            case (ComponentParameterKind.PositiveWidth, _):
                diagnostics.Add(InvalidParameter(contractKey, schema.Id, "parameterKind"));
                return;
            case (ComponentParameterKind.Choice, ChoiceParameterValue choice):
                if (!schema.AllowedValues.Contains(choice.Value, StringComparer.Ordinal))
                {
                    diagnostics.Add(InvalidParameter(contractKey, schema.Id, "allowedValue"));
                }

                return;
            case (ComponentParameterKind.LogicVector, LogicVectorParameterValue vector):
                ValidateLogicVector(
                    contractKey,
                    schema,
                    vector,
                    allParameters,
                    diagnostics);
                return;
            default:
                diagnostics.Add(InvalidParameter(contractKey, schema.Id, "parameterKind"));
                return;
        }
    }

    private static void ValidateLogicVector(
        ComponentContractKey contractKey,
        ComponentParameterSchema schema,
        LogicVectorParameterValue vector,
        ReadOnlyCollection<ComponentParameterBinding> allParameters,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (vector.Values.Count == 0
            || vector.Values.Any(value => value is < LogicValue.Zero or > LogicValue.X))
        {
            diagnostics.Add(InvalidParameter(contractKey, schema.Id, "logicVectorValue"));
            return;
        }

        var width = allParameters
            .Where(binding => string.Equals(
                binding.ParameterId,
                schema.WidthParameterId,
                StringComparison.Ordinal))
            .Select(binding => binding.Value)
            .OfType<Unsigned32ParameterValue>()
            .Select(value => value.Value)
            .FirstOrDefault();

        if (width == 0 || vector.Values.Count != width)
        {
            diagnostics.Add(InvalidParameter(contractKey, schema.Id, "vectorWidth"));
        }
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
