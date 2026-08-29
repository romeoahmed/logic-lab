using System.Diagnostics.CodeAnalysis;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record ResolvedBoundarySymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string Label,
    PortDirection PortDirection,
    string DeviationCode);

internal static class TeachingMixedBoundarySymbolRegistry
{
    public static bool TryResolve(
        string contractId,
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string? requestedVariantId,
        [NotNullWhen(true)] out ResolvedBoundarySymbolDefinition? resolved)
    {
        if (requestedVariantId is not (null or SymbolVariantCatalog.BoundaryId)
            || ports is not [var port])
        {
            resolved = null;
            return false;
        }

        resolved = contractId switch
        {
            "source.input" when port.Direction == PortDirection.Output =>
                Create(contractId, "[IN]", PortDirection.Output),
            "sink.output" when port.Direction == PortDirection.Input =>
                Create(contractId, "[OUT]", PortDirection.Input),
            _ => null,
        };
        return resolved is not null;
    }

    private static ResolvedBoundarySymbolDefinition Create(
        string contractId,
        string label,
        PortDirection direction) => new(
            $"logiclab.teachingmixed.{contractId}.boundary",
            "1.0.0",
            label,
            direction,
            $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}");
}
