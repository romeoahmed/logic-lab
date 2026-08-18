using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal enum RectangularSymbolFunctionRecipe
{
    Literal,
    BinaryDecoder,
    PriorityEncoder,
    Shift,
}

internal sealed record RectangularSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string AccessibilityKey,
    RectangularSymbolFunctionRecipe FunctionRecipe,
    string? LiteralFunctionText,
    ConformanceClaimV1 Claim,
    string[] StandardClauses,
    string? FunctionDeviationCode,
    RectangularSymbolDependencyRecipe DependencyRecipe);

internal sealed record ResolvedRectangularSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string AccessibilityKey,
    string VariantId,
    string FunctionText,
    FontRoleV1 FunctionFontRole,
    ConformanceClaimV1 Claim,
    string[] StandardClauses,
    ConformanceDeviationV1[] Deviations,
    RectangularSymbolDependency[] Dependencies);

internal static class TeachingMixedRectangularSymbolRegistry
{
    private static readonly FrozenDictionary<string, RectangularSymbolDefinition> Definitions =
        new Dictionary<string, RectangularSymbolDefinition>(
        [
            Extension("source.input", "[IN]", ConformanceClaimV1.TeachingExtension),
            Extension("source.constant", "[CONST]", ConformanceClaimV1.TeachingExtension),
            Extension("sink.output", "[OUT]", ConformanceClaimV1.TeachingExtension),
            Extension("topology.split", "[SPLIT]"),
            Extension("topology.concat", "[CONCAT]"),
            Extension("topology.zero_extend", "[ZERO EXT]"),
            Extension("topology.sign_extend", "[SIGN EXT]"),
            Standard(
                "logic.tristate",
                "1",
                ["3.3-8", "3.3-12", "4.3.9", "5.2-4"],
                RectangularSymbolDependencyRecipe.EnableOutputs),
            Standard(
                "logic.mux",
                "MUX",
                ["4.3.2", "4.4.2", "5.6-1"],
                RectangularSymbolDependencyRecipe.SelectDataInputs),
            Standard(
                "logic.demux",
                "DX",
                ["4.3.2", "4.4.2", "5.6-2"],
                RectangularSymbolDependencyRecipe.SelectDataOutputs),
            DynamicStandard(
                "logic.decoder",
                RectangularSymbolFunctionRecipe.BinaryDecoder,
                ["4.3.9", "5.4-1", "5.4-4"],
                RectangularSymbolDependencyRecipe.EnableOutputs),
            PriorityEncoder(),
            Standard(
                "logic.unsigned_compare",
                "COMP",
                ["3.3-31", "3.3-32", "3.3-33", "5.7-1", "5.7-11"]),
            Standard("logic.adder", "Σ", ["3.3-25", "3.3-26", "5.7-1", "5.7-5"]),
            Standard(
                "logic.subtractor",
                "P-Q",
                ["3.3-25", "3.3-26", "5.7-1", "5.7-6"]),
            DynamicExtension(
                "logic.shift",
                RectangularSymbolFunctionRecipe.Shift),
        ],
        StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);

    public static bool TryResolve(
        string contractId,
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string? requestedVariantId,
        [NotNullWhen(true)] out ResolvedRectangularSymbolDefinition? resolved)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(ports);
        if (!Definitions.TryGetValue(contractId, out var definition)
            || requestedVariantId is not (null or SymbolVariantCatalog.RectangularId))
        {
            resolved = null;
            return false;
        }

        var functionText = FunctionText(definition, parameters);
        var functionFontRole = definition.FunctionDeviationCode is not null
            || definition.FunctionRecipe == RectangularSymbolFunctionRecipe.PriorityEncoder
                ? FontRoleV1.ExtensionMark
                : FontRoleV1.Symbol;
        var deviations = new List<ConformanceDeviationV1>();
        if (definition.FunctionDeviationCode is { } functionDeviationCode)
        {
            deviations.Add(new ConformanceDeviationV1(functionDeviationCode, []));
        }

        if (definition.FunctionRecipe == RectangularSymbolFunctionRecipe.PriorityEncoder)
        {
            var priority = Choice(parameters, "priority");
            deviations.Add(new ConformanceDeviationV1(
                "teachingmixed-unmodeled-priority-encoder",
                [.. ports.Select(port => port.Id)]));
            if (priority == "lowestIndex")
            {
                deviations.Add(new ConformanceDeviationV1(
                    "teachingmixed-lowest-priority-encoder",
                    [.. ports.Select(port => port.Id)]));
            }
        }

        resolved = new ResolvedRectangularSymbolDefinition(
            definition.DefinitionId,
            definition.DefinitionVersion,
            definition.AccessibilityKey,
            SymbolVariantCatalog.RectangularId,
            functionText,
            functionFontRole,
            definition.Claim,
            definition.StandardClauses,
            [.. deviations],
            RectangularSymbolDependencyResolver.Resolve(
                definition.DependencyRecipe,
                ports));
        return true;
    }

    private static KeyValuePair<string, RectangularSymbolDefinition> Standard(
        string contractId,
        string functionText,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None) => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.Literal,
            functionText,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe);

    private static KeyValuePair<string, RectangularSymbolDefinition> PriorityEncoder() =>
        Definition(
            "logic.priority_encoder",
            RectangularSymbolFunctionRecipe.PriorityEncoder,
            literalFunctionText: null,
            ConformanceClaimV1.TeachingExtension,
            ["5.4.1.2", "5.4-6"],
            functionDeviationCode: null,
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicStandard(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None) => Definition(
            contractId,
            functionRecipe,
            literalFunctionText: null,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe);

    private static KeyValuePair<string, RectangularSymbolDefinition> Extension(
        string contractId,
        string functionText,
        ConformanceClaimV1 claim = ConformanceClaimV1.StandardBaseWithNonstandardInfo) => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.Literal,
            functionText,
            claim,
            ["2.1.2", "2.2"],
            $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}",
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicExtension(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        ConformanceClaimV1 claim = ConformanceClaimV1.StandardBaseWithNonstandardInfo) => Definition(
            contractId,
            functionRecipe,
            literalFunctionText: null,
            claim,
            ["2.1.2", "2.2"],
            $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}",
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> Definition(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        string? literalFunctionText,
        ConformanceClaimV1 claim,
        string[] clauses,
        string? functionDeviationCode,
        RectangularSymbolDependencyRecipe dependencyRecipe) => KeyValuePair.Create(
            contractId,
            new RectangularSymbolDefinition(
                $"logiclab.teachingmixed.{contractId}",
                "3.0.0",
                $"presentation.symbol.{contractId}",
                functionRecipe,
                literalFunctionText,
                claim,
                clauses,
                functionDeviationCode,
                dependencyRecipe));

    private static string FunctionText(
        RectangularSymbolDefinition definition,
        IReadOnlyList<ComponentParameterBinding> parameters) =>
        definition.FunctionRecipe switch
        {
            RectangularSymbolFunctionRecipe.Literal =>
                definition.LiteralFunctionText
                    ?? throw new InvalidOperationException(
                        "A literal rectangular function has no text."),
            RectangularSymbolFunctionRecipe.BinaryDecoder => string.Concat(
                "BIN/",
                CheckedPowerOfTwo(U32(parameters, "selectorWidth")).ToString(
                    CultureInfo.InvariantCulture)),
            RectangularSymbolFunctionRecipe.PriorityEncoder =>
                Choice(parameters, "priority") == "highestIndex"
                    ? "[HPRI/BIN]"
                    : "[LPRI/BIN]",
            RectangularSymbolFunctionRecipe.Shift =>
                Choice(parameters, "direction") == "left" ? "[SHL]" : "[SHR]",
            _ => throw new InvalidOperationException(
                "The rectangular function recipe is undefined."),
        };

    private static uint U32(
        IReadOnlyList<ComponentParameterBinding> parameters,
        string parameterId) => parameters.Single(parameter => parameter.ParameterId == parameterId)
            .Value is Unsigned32ParameterValue value
                ? value.Value
                : throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind);

    private static string Choice(
        IReadOnlyList<ComponentParameterBinding> parameters,
        string parameterId) => parameters.Single(parameter => parameter.ParameterId == parameterId)
            .Value is ChoiceParameterValue value
                ? value.Value
                : throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind);

    private static uint CheckedPowerOfTwo(uint exponent)
    {
        if (exponent >= 32)
        {
            throw new OverflowException();
        }

        return 1U << checked((int)exponent);
    }
}
