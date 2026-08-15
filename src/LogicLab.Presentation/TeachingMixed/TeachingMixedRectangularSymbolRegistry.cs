using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record RectangularSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string AccessibilityKey,
    Func<IReadOnlyList<ComponentParameterBinding>, string> FunctionText,
    ConformanceClaimV1 Claim,
    ReadOnlyCollection<string> StandardClauses,
    string? DeviationCode,
    RectangularSymbolDependencyRecipe DependencyRecipe);

internal sealed record ResolvedRectangularSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string AccessibilityKey,
    string VariantId,
    string FunctionText,
    ConformanceClaimV1 Claim,
    ReadOnlyCollection<string> StandardClauses,
    string? DeviationCode,
    ReadOnlyCollection<RectangularSymbolDependency> Dependencies);

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
                parameters => string.Concat(
                    "BIN/",
                        CheckedPowerOfTwo(U32(parameters, "selectorWidth")).ToString(
                            CultureInfo.InvariantCulture)),
                ["4.3.9", "5.4-1", "5.4-4"],
                RectangularSymbolDependencyRecipe.EnableOutputs),
            Standard("logic.priority_encoder", "HPRI/BIN", ["5.4.1.2", "5.4-6"]),
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
                parameters => Choice(parameters, "direction") == "left" ? "[SHL]" : "[SHR]"),
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

        resolved = new ResolvedRectangularSymbolDefinition(
            definition.DefinitionId,
            definition.DefinitionVersion,
            definition.AccessibilityKey,
            SymbolVariantCatalog.RectangularId,
            definition.FunctionText(parameters),
            definition.Claim,
            definition.StandardClauses,
            definition.DeviationCode,
            RectangularSymbolDependencyResolver.Resolve(
                definition.DependencyRecipe,
                ports));
        return true;
    }

    private static KeyValuePair<string, RectangularSymbolDefinition> Standard(
        string contractId,
        string functionText,
        IReadOnlyList<string> clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None) => DynamicStandard(
            contractId,
            _ => functionText,
            clauses,
            dependencyRecipe);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicStandard(
        string contractId,
        Func<IReadOnlyList<ComponentParameterBinding>, string> functionText,
        IReadOnlyList<string> clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None) => Definition(
            contractId,
            functionText,
            ConformanceClaimV1.Standardized91A,
            clauses,
            deviationCode: null,
            dependencyRecipe);

    private static KeyValuePair<string, RectangularSymbolDefinition> Extension(
        string contractId,
        string functionText,
        ConformanceClaimV1 claim = ConformanceClaimV1.StandardBaseWithNonstandardInfo) =>
        DynamicExtension(contractId, _ => functionText, claim);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicExtension(
        string contractId,
        Func<IReadOnlyList<ComponentParameterBinding>, string> functionText,
        ConformanceClaimV1 claim = ConformanceClaimV1.StandardBaseWithNonstandardInfo) => Definition(
            contractId,
            functionText,
            claim,
            ["2.1.2", "2.2"],
            $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}",
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> Definition(
        string contractId,
        Func<IReadOnlyList<ComponentParameterBinding>, string> functionText,
        ConformanceClaimV1 claim,
        IReadOnlyList<string> clauses,
        string? deviationCode,
        RectangularSymbolDependencyRecipe dependencyRecipe) => KeyValuePair.Create(
            contractId,
            new RectangularSymbolDefinition(
                $"logiclab.teachingmixed.{contractId}",
                dependencyRecipe == RectangularSymbolDependencyRecipe.None
                    ? "1.0.0"
                    : "2.0.0",
                $"presentation.symbol.{contractId}",
                functionText,
                claim,
                Array.AsReadOnly(clauses.ToArray()),
                deviationCode,
                dependencyRecipe));

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
