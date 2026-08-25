using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Scene;

public sealed record ScenePlaceOptionV1(
    string Id,
    string Label,
    ScenePlaceToolV1 Tool);

public static class ScenePlaceCatalog
{
    public static IReadOnlyList<ScenePlaceOptionV1> Build(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var options = new List<ScenePlaceOptionV1>();
        foreach (var contract in CoreLibrarySchema.Contracts)
        {
            if (!TryCreateDefaultParameters(contract, out var parameters))
            {
                continue;
            }

            options.Add(new ScenePlaceOptionV1(
                $"library:{contract.Key.LibraryId}:{contract.Key.ContractId}",
                contract.Key.ContractId,
                new ScenePlaceToolV1(
                    new SceneLibraryComponentTargetV1(
                        contract.Key.LibraryId,
                        contract.Key.ContractId),
                    [.. parameters.Select(ToSceneParameter)],
                    null,
                    pinned: false)));
        }

        options.AddRange(document.CircuitDefinitions
            .OrderBy(definition => definition.DisplayName, StringComparer.Ordinal)
            .ThenBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .Select(definition => new ScenePlaceOptionV1(
                $"definition:{definition.Id.Value}",
                definition.DisplayName,
                new ScenePlaceToolV1(
                    new SceneCircuitDefinitionTargetV1(definition.Id.Value),
                    [],
                    null,
                    pinned: false))));
        return options;
    }

    private static bool TryCreateDefaultParameters(
        ComponentContractSchema contract,
        out IReadOnlyList<ComponentParameterBinding> parameters)
    {
        if (contract.Parameters.Any(parameter =>
            parameter.Kind == ComponentParameterKind.MemoryImage))
        {
            parameters = [];
            return false;
        }

        var bindings = new List<ComponentParameterBinding>(contract.Parameters.Count);
        foreach (var parameter in contract.Parameters)
        {
            bindings.Add(new ComponentParameterBinding(
                parameter.Id,
                CreateDefaultValue(parameter, bindings)));
        }

        try
        {
            _ = contract.ResolvePorts(bindings);
            parameters = bindings;
            return true;
        }
        catch (ArgumentException)
        {
            parameters = [];
            return false;
        }
    }

    private static ComponentParameterValue CreateDefaultValue(
        ComponentParameterSchema schema,
        IReadOnlyList<ComponentParameterBinding> bindings)
    {
        return schema.Kind switch
        {
            ComponentParameterKind.PositiveWidth => new Unsigned32ParameterValue(
                PositiveWidth(schema, bindings)),
            ComponentParameterKind.LogicVector => new LogicVectorParameterValue(
                [.. Enumerable.Repeat(
                    LogicValue.Zero,
                    checked((int)(schema.FixedWidth ?? Width(bindings, schema.WidthParameterId))))]),
            ComponentParameterKind.Choice => new ChoiceParameterValue(
                schema.AllowedValues.First()),
            ComponentParameterKind.Slices => new SlicesParameterValue(
                [.. Enumerable.Repeat(
                    new BitSlice(0, 1),
                    Math.Max(1, schema.MinimumItemCount))]),
            ComponentParameterKind.Widths => new WidthsParameterValue(
                [.. Enumerable.Repeat(1u, Math.Max(1, schema.MinimumItemCount))]),
            ComponentParameterKind.BinaryLogicValue => new LogicVectorParameterValue(
                [LogicValue.Zero]),
            ComponentParameterKind.PositiveUnsigned64 => new Unsigned64ParameterValue(1),
            ComponentParameterKind.MemoryImage => throw new InvalidOperationException(
                "Memory-backed contracts require an explicit Memory Image selection."),
            _ => throw new InvalidOperationException(
                "The component parameter kind is undefined."),
        };
    }

    private static uint PositiveWidth(
        ComponentParameterSchema schema,
        IReadOnlyList<ComponentParameterBinding> bindings)
    {
        var minimum = schema.MinimumValue;
        if (schema.GreaterThanParameterId is null)
        {
            return minimum;
        }

        return checked(Math.Max(
            minimum,
            Width(bindings, schema.GreaterThanParameterId) + 1));
    }

    private static uint Width(
        IEnumerable<ComponentParameterBinding> bindings,
        string? parameterId) => bindings
            .Where(binding => string.Equals(
                binding.ParameterId,
                parameterId,
                StringComparison.Ordinal))
            .Select(binding => binding.Value)
            .OfType<Unsigned32ParameterValue>()
            .Select(value => value.Value)
            .Single();

    private static SceneParameterBindingV1 ToSceneParameter(
        ComponentParameterBinding binding) => new(
            binding.ParameterId,
            binding.Value switch
            {
                Unsigned32ParameterValue value => new SceneUnsigned32ParameterV1(value.Value),
                Unsigned64ParameterValue value => new SceneUnsigned64ParameterV1(
                    value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ChoiceParameterValue value => new SceneChoiceParameterV1(value.Value),
                LogicVectorParameterValue value => new SceneLogicVectorParameterV1(
                    string.Concat(value.Values.Select(LogicToken))),
                WidthsParameterValue value => new SceneWidthsParameterV1(value.Values),
                SlicesParameterValue value => new SceneSlicesParameterV1(
                    [.. value.Values.Select(slice => new SceneBitSliceV1(
                        slice.Offset,
                        slice.Length))]),
                _ => throw new InvalidOperationException(
                    "The catalog parameter value is undefined."),
            });

    private static char LogicToken(LogicValue value) => value switch
    {
        LogicValue.Zero => '0',
        LogicValue.One => '1',
        LogicValue.X => 'X',
        LogicValue.Z => 'Z',
        _ => throw new InvalidOperationException("The Logic Value is undefined."),
    };
}
