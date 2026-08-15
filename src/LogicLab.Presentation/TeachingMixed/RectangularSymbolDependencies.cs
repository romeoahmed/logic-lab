using System.Collections.ObjectModel;
using System.Globalization;
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

internal sealed record RectangularSymbolAffectedEndpoint
{
    public RectangularSymbolAffectedEndpoint(string portId, int applicationOrder)
    {
        ArgumentException.ThrowIfNullOrEmpty(portId);
        ArgumentOutOfRangeException.ThrowIfNegative(applicationOrder);
        PortId = portId;
        ApplicationOrder = applicationOrder;
    }

    public string PortId { get; }

    public int ApplicationOrder { get; }
}

internal sealed record RectangularSymbolDependency
{
    public RectangularSymbolDependency(
        RectangularSymbolDependencyKind kind,
        string identifier,
        string affectingPortId,
        IReadOnlyList<RectangularSymbolAffectedEndpoint> affectedEndpoints)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ArgumentException.ThrowIfNullOrEmpty(affectingPortId);
        ArgumentNullException.ThrowIfNull(affectedEndpoints);
        if (identifier.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                "A supported dependency identifier must use decimal digits.",
                nameof(identifier));
        }

        if (affectedEndpoints.Count == 0)
        {
            throw new ArgumentException(
                "A dependency must affect at least one Port.",
                nameof(affectedEndpoints));
        }

        Kind = kind;
        Identifier = identifier;
        AffectingPortId = affectingPortId;
        AffectedEndpoints = Array.AsReadOnly(affectedEndpoints.ToArray());
    }

    public RectangularSymbolDependencyKind Kind { get; }

    public string Identifier { get; }

    public string AffectingPortId { get; }

    public ReadOnlyCollection<RectangularSymbolAffectedEndpoint> AffectedEndpoints { get; }
}

internal static class RectangularSymbolDependencyResolver
{
    public static ReadOnlyCollection<RectangularSymbolDependency> Resolve(
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
        return Array.AsReadOnly(dependencies);
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
                "1",
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
                index.ToString(CultureInfo.InvariantCulture),
                selectorPortId,
                [new RectangularSymbolAffectedEndpoint(affected[index].Id, 0)]);
        }

        return dependencies;
    }
}
