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
    string ContractId,
    string DefinitionId,
    string DefinitionVersion,
    BasicOutlineRecipe DistinctiveRecipe,
    bool HasOutputQualifier,
    string DistinctiveClause,
    string RectangularFunction,
    string AccessibilityKey);

internal sealed record ResolvedBasicSymbolDefinition(
    BasicSymbolDefinition Definition,
    BasicOutlineRecipe Recipe,
    string VariantId,
    string FunctionText,
    ConformanceClaimV1 Claim,
    string PrimaryClause,
    AnnexAStatusV1 AnnexA);

internal static class TeachingMixedBasicSymbolRegistry
{
    private static readonly Dictionary<string, BasicSymbolDefinition> Definitions =
        new Dictionary<string, BasicSymbolDefinition>(StringComparer.Ordinal)
        {
            ["logic.and"] = Definition(
                "logic.and",
                BasicOutlineRecipe.And,
                hasOutputQualifier: false,
                "5.1-3",
                "&"),
            ["logic.nand"] = Definition(
                "logic.nand",
                BasicOutlineRecipe.And,
                hasOutputQualifier: true,
                "5.1-17",
                "&"),
            ["logic.or"] = Definition(
                "logic.or",
                BasicOutlineRecipe.Or,
                hasOutputQualifier: false,
                "5.1-1",
                "\u22651"),
            ["logic.nor"] = Definition(
                "logic.nor",
                BasicOutlineRecipe.Or,
                hasOutputQualifier: true,
                "5.1-18",
                "\u22651"),
            ["logic.xor"] = Definition(
                "logic.xor",
                BasicOutlineRecipe.Xor,
                hasOutputQualifier: false,
                "5.1-11",
                "=1"),
            ["logic.xnor"] = Definition(
                "logic.xnor",
                BasicOutlineRecipe.Xor,
                hasOutputQualifier: true,
                "5.1-11",
                "=1"),
            ["logic.buffer"] = Definition(
                "logic.buffer",
                BasicOutlineRecipe.Triangle,
                hasOutputQualifier: false,
                "5.1-12",
                "1"),
            ["logic.not"] = Definition(
                "logic.not",
                BasicOutlineRecipe.Triangle,
                hasOutputQualifier: true,
                "5.1-13",
                "1"),
        };

    public static bool TryResolve(
        string contractId,
        int inputCount,
        string? requestedVariantId,
        IndicationConvention indicationConvention,
        out ResolvedBasicSymbolDefinition? resolved,
        out string? diagnosticCode)
    {
        if (!Definitions.TryGetValue(contractId, out var definition))
        {
            resolved = null;
            diagnosticCode = "layout_symbol_unsupported";
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
            diagnosticCode = "layout_symbol_variant_unknown";
            return false;
        }

        if (variantId == SymbolVariantCatalog.DistinctiveId && defaultRectangle)
        {
            resolved = null;
            diagnosticCode = "layout_symbol_variant_incompatible";
            return false;
        }

        var recipe = variantId == SymbolVariantCatalog.RectangularId
            ? BasicOutlineRecipe.Rectangle
            : definition.DistinctiveRecipe;
        var functionText = definition.RectangularFunction;
        var clause = definition.DistinctiveClause;
        if (recipe == BasicOutlineRecipe.Rectangle
            && definition.DistinctiveRecipe == BasicOutlineRecipe.Xor
            && inputCount > 2)
        {
            var oddParity = !definition.HasOutputQualifier;
            functionText = oddParity ? "2k+1" : "2k";
            clause = oddParity ? "5.1-9" : "5.1-10";
        }

        if (indicationConvention == IndicationConvention.DirectPolarity
            && definition.ContractId == "logic.not")
        {
            clause = "5.1-14";
        }

        resolved = new ResolvedBasicSymbolDefinition(
            definition,
            recipe,
            variantId,
            functionText,
            recipe == BasicOutlineRecipe.Rectangle
                ? ConformanceClaimV1.Standardized91A
                : ConformanceClaimV1.PermittedDistinctive91A,
            clause,
            recipe == BasicOutlineRecipe.Rectangle
                ? AnnexAStatusV1.NotEvaluated
                : AnnexAStatusV1.Pass);
        diagnosticCode = null;
        return true;
    }

    private static BasicSymbolDefinition Definition(
        string contractId,
        BasicOutlineRecipe recipe,
        bool hasOutputQualifier,
        string distinctiveClause,
        string rectangularFunction) => new(
            contractId,
            $"logiclab.teachingmixed.{contractId}",
            "1.0.0",
            recipe,
            hasOutputQualifier,
            distinctiveClause,
            rectangularFunction,
            $"presentation.symbol.{contractId}");
}
