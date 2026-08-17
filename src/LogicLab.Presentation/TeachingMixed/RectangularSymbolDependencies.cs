using LogicLab.Domain.Components;

namespace LogicLab.Presentation.TeachingMixed;

internal enum RectangularSymbolDependencyKind
{
    And,
    Enable,
}

internal enum RectangularSymbolDependencyRecipe
{
    None,
    EnableOutputs,
    SelectDataInputs,
    SelectDataOutputs,
}

internal readonly record struct RectangularSymbolAffectedEndpoint(
    string PortId,
    int ApplicationOrder);

internal sealed record RectangularSymbolDependency(
    RectangularSymbolDependencyKind Kind,
    uint Identifier,
    string AffectingPortId,
    RectangularSymbolAffectedEndpoint[] AffectedEndpoints);

internal static class RectangularSymbolDependencyResolver
{
    public static RectangularSymbolDependency[] Resolve(
        RectangularSymbolDependencyRecipe recipe,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var dependencies = recipe switch
        {
            RectangularSymbolDependencyRecipe.None => [],
            RectangularSymbolDependencyRecipe.EnableOutputs => EnableOutputs(ports),
            RectangularSymbolDependencyRecipe.SelectDataInputs => SelectDataPorts(
                ports,
                PortDirection.Input,
                "S"),
            RectangularSymbolDependencyRecipe.SelectDataOutputs => SelectDataPorts(
                ports,
                PortDirection.Output,
                "S"),
            _ => throw new ArgumentOutOfRangeException(nameof(recipe)),
        };
        return dependencies;
    }

    private static RectangularSymbolDependency[] EnableOutputs(
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        var affected = ports
            .Where(port => port.Direction == PortDirection.Output)
            .Select(port => new RectangularSymbolAffectedEndpoint(port.Id, 0))
            .ToArray();
        return
        [
            new RectangularSymbolDependency(
                RectangularSymbolDependencyKind.Enable,
                1,
                "EN",
                affected),
        ];
    }

    private static RectangularSymbolDependency[] SelectDataPorts(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        PortDirection affectedDirection,
        string selectorPortId)
    {
        var affected = ports
            .Where(port => port.Direction == affectedDirection && port.Id != selectorPortId)
            .ToArray();
        var dependencies = new RectangularSymbolDependency[affected.Length];
        for (var index = 0; index < affected.Length; index++)
        {
            dependencies[index] = new RectangularSymbolDependency(
                RectangularSymbolDependencyKind.And,
                checked((uint)index),
                selectorPortId,
                [new RectangularSymbolAffectedEndpoint(affected[index].Id, 0)]);
        }

        return dependencies;
    }
}
