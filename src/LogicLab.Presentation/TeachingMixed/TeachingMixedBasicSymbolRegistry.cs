using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal enum BasicOutlineRecipe
{
    And,
    Or,
    Xor,
    Triangle,
    Rectangle,
}

internal sealed record BasicSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    BasicOutlineRecipe DistinctiveRecipe,
    bool HasOutputQualifier,
    string NegationPrimaryClause,
    string DirectPolarityPrimaryClause,
    string RectangularFunction,
    string AccessibilityKey);

internal sealed record ResolvedBasicSymbolDefinition(
    BasicSymbolDefinition Definition,
    BasicOutlineRecipe Recipe,
    string VariantId,
    string FunctionText,
    bool HasOutputQualifier,
    ConformanceClaimV1 Claim,
    ReadOnlyCollection<string> StandardClauses,
    AnnexAStatusV1 AnnexA);

internal static class TeachingMixedBasicSymbolRegistry
{
    private static readonly FrozenDictionary<string, BasicSymbolDefinition> Definitions =
        new Dictionary<string, BasicSymbolDefinition>
        {
            ["logic.and"] = Definition(
                "logic.and",
                BasicOutlineRecipe.And,
                hasOutputQualifier: false,
                "5.1-3",
                "5.1-3",
                "&"),
            ["logic.nand"] = Definition(
                "logic.nand",
                BasicOutlineRecipe.And,
                hasOutputQualifier: true,
                "5.1-17",
                "5.1-3",
                "&"),
            ["logic.or"] = Definition(
                "logic.or",
                BasicOutlineRecipe.Or,
                hasOutputQualifier: false,
                "5.1-1",
                "5.1-1",
                "\u22651"),
            ["logic.nor"] = Definition(
                "logic.nor",
                BasicOutlineRecipe.Or,
                hasOutputQualifier: true,
                "5.1-1",
                "5.1-18",
                "\u22651"),
            ["logic.xor"] = Definition(
                "logic.xor",
                BasicOutlineRecipe.Xor,
                hasOutputQualifier: false,
                "5.1-11",
                "5.1-11",
                "=1"),
            ["logic.xnor"] = Definition(
                "logic.xnor",
                BasicOutlineRecipe.Xor,
                hasOutputQualifier: true,
                "5.1-11",
                "5.1-11",
                "=1"),
            ["logic.buffer"] = Definition(
                "logic.buffer",
                BasicOutlineRecipe.Triangle,
                hasOutputQualifier: false,
                "5.1-12",
                "5.1-12",
                "1"),
            ["logic.not"] = Definition(
                "logic.not",
                BasicOutlineRecipe.Triangle,
                hasOutputQualifier: true,
                "5.1-13",
                "5.1-14",
                "1"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool TryResolve(
        string contractId,
        int inputCount,
        string? requestedVariantId,
        IndicationConvention indicationConvention,
        SymbolFacingV1 facing,
        [NotNullWhen(true)] out ResolvedBasicSymbolDefinition? resolved)
    {
        if (!Definitions.TryGetValue(contractId, out var definition))
        {
            resolved = null;
            return false;
        }

        var defaultRectangle = definition.DistinctiveRecipe == BasicOutlineRecipe.Xor
            && inputCount > 2;
        var variantId = requestedVariantId
            ?? (defaultRectangle
                ? SymbolVariantCatalog.RectangularId
                : SymbolVariantCatalog.DistinctiveId);
        if (variantId is not (SymbolVariantCatalog.DistinctiveId
            or SymbolVariantCatalog.RectangularId))
        {
            resolved = null;
            return false;
        }

        if (variantId == SymbolVariantCatalog.DistinctiveId && defaultRectangle)
        {
            resolved = null;
            return false;
        }

        var recipe = variantId == SymbolVariantCatalog.RectangularId
            ? BasicOutlineRecipe.Rectangle
            : definition.DistinctiveRecipe;
        var functionText = definition.RectangularFunction;
        var primaryClause = indicationConvention == IndicationConvention.Negation
            ? definition.NegationPrimaryClause
            : definition.DirectPolarityPrimaryClause;
        var usesParityFunction = recipe == BasicOutlineRecipe.Rectangle
            && definition.DistinctiveRecipe == BasicOutlineRecipe.Xor
            && inputCount > 2;
        if (usesParityFunction)
        {
            var oddParity = !definition.HasOutputQualifier;
            functionText = oddParity ? "2k+1" : "2k";
            primaryClause = oddParity ? "5.1-9" : "5.1-10";
        }

        var hasOutputQualifier = definition.HasOutputQualifier && !usesParityFunction;
        var standardClauses = new List<string> { primaryClause };
        if (hasOutputQualifier)
        {
            standardClauses.Add("3.1.1");
            standardClauses.Add(indicationConvention switch
            {
                IndicationConvention.Negation => "3.1-2",
                IndicationConvention.DirectPolarity when facing == SymbolFacingV1.West =>
                    "3.1-7",
                IndicationConvention.DirectPolarity => "3.1-6",
                _ => throw new ArgumentOutOfRangeException(nameof(indicationConvention)),
            });
        }

        resolved = new ResolvedBasicSymbolDefinition(
            definition,
            recipe,
            variantId,
            functionText,
            hasOutputQualifier,
            recipe == BasicOutlineRecipe.Rectangle
                ? ConformanceClaimV1.Standardized91A
                : ConformanceClaimV1.PermittedDistinctive91A,
            Array.AsReadOnly(standardClauses.ToArray()),
            recipe == BasicOutlineRecipe.Rectangle
                ? AnnexAStatusV1.NotEvaluated
                : AnnexAStatusV1.Pass);
        return true;
    }

    private static BasicSymbolDefinition Definition(
        string contractId,
        BasicOutlineRecipe recipe,
        bool hasOutputQualifier,
        string negationPrimaryClause,
        string directPolarityPrimaryClause,
        string rectangularFunction) => new(
            $"logiclab.teachingmixed.{contractId}",
            "1.2.0",
            recipe,
            hasOutputQualifier,
            negationPrimaryClause,
            directPolarityPrimaryClause,
            rectangularFunction,
            $"presentation.symbol.{contractId}");
}
