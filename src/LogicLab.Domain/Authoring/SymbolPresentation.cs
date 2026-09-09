namespace LogicLab.Domain.Authoring;

public enum IndicationConvention
{
    Negation,
    DirectPolarity,
}

public sealed record SymbolProfileReference(
    string Id,
    string Version,
    IndicationConvention IndicationConvention);

public static class SymbolProfileRegistry
{
    public static bool IsRegistered(SymbolProfileReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return string.Equals(reference.Id, "TeachingMixed", StringComparison.Ordinal)
            && string.Equals(reference.Version, "1.0.0", StringComparison.Ordinal)
            && Enum.IsDefined(reference.IndicationConvention);
    }
}

public static class SymbolVariantCatalog
{
    public const string BoundaryId = "logiclab.teachingmixed.boundary";
    public const string DistinctiveId = "logiclab.teachingmixed.distinctive";
    public const string RectangularId = "logiclab.teachingmixed.rectangular";

    public static IReadOnlyList<string> GetCompatibleVariants(
        SymbolProfileReference profile, ComponentTarget target, IReadOnlyList<ComponentParameterBinding> parameters)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parameters);
        string[] variants = [BoundaryId, DistinctiveId, RectangularId];
        return Array.AsReadOnly(variants.Where(variant => IsCompatible(profile, target, parameters, variant)).ToArray());
    }

    internal static bool IsCompatible(
        SymbolProfileReference profile,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        string variantId)
    {
        if (!SymbolProfileRegistry.IsRegistered(profile))
        {
            return false;
        }

        if (string.Equals(variantId, RectangularId, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(variantId, BoundaryId, StringComparison.Ordinal))
        {
            return target is LibraryComponentTarget boundary
                && boundary.ContractKey.ContractId is "source.input" or "sink.output";
        }

        if (!string.Equals(variantId, DistinctiveId, StringComparison.Ordinal)
            || target is not LibraryComponentTarget library)
        {
            return false;
        }

        if (library.ContractKey.ContractId is "logic.buffer" or "logic.not")
        {
            return true;
        }

        if (library.ContractKey.ContractId is not (
            "logic.and" or "logic.nand" or "logic.or" or "logic.nor"
            or "logic.xor" or "logic.xnor"))
        {
            return false;
        }

        var fanIn = parameters
            .FirstOrDefault(parameter => parameter.ParameterId == "fanIn")?
            .Value as Unsigned32ParameterValue;
        return library.ContractKey.ContractId is not ("logic.xor" or "logic.xnor")
            || fanIn?.Value == 2;
    }
}
