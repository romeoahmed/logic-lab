using LogicLab.Domain.Components;

namespace LogicLab.Presentation.TeachingMixed;

internal enum RectangularSymbolDependencyKind
{
    And,
    Enable,
    Control,
    Mode,
    Address,
}

internal enum RectangularSymbolDependencyRecipe
{
    None,
    EnableOutputs,
    SelectDataInputs,
    SelectDataOutputs,
    StorageEnable,
    ClockedData,
    ClockedJk,
    ClockedToggle,
    ClockedRegister,
    ShiftRegister,
    Counter,
    ReadOnlyMemory,
    SinglePortMemory,
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
            RectangularSymbolDependencyRecipe.StorageEnable =>
                Single(ports, RectangularSymbolDependencyKind.And, 1, "EN", "D"),
            RectangularSymbolDependencyRecipe.ClockedData =>
                Single(ports, RectangularSymbolDependencyKind.Control, 1, "CLK", "D"),
            RectangularSymbolDependencyRecipe.ClockedJk =>
                Single(ports, RectangularSymbolDependencyKind.Control, 1, "CLK", "J", "K"),
            RectangularSymbolDependencyRecipe.ClockedToggle =>
                Single(ports, RectangularSymbolDependencyKind.Control, 1, "CLK", "T"),
            RectangularSymbolDependencyRecipe.ClockedRegister =>
            [
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    1,
                    "CLK",
                    "D"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.And,
                    2,
                    "EN",
                    "D"),
            ],
            RectangularSymbolDependencyRecipe.ShiftRegister =>
            [
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Mode,
                    1,
                    "LOAD",
                    "PARALLEL"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    2,
                    "CLK",
                    "PARALLEL",
                    "SERIAL"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.And,
                    3,
                    "EN",
                    "PARALLEL",
                    "SERIAL"),
            ],
            RectangularSymbolDependencyRecipe.Counter =>
            [
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Mode,
                    1,
                    "LOAD",
                    "LOAD_VALUE"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    2,
                    "CLK",
                    "LOAD_VALUE"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.And,
                    3,
                    "EN",
                    "LOAD_VALUE"),
            ],
            RectangularSymbolDependencyRecipe.ReadOnlyMemory =>
                Single(ports, RectangularSymbolDependencyKind.Address, 1, "A", "Q"),
            RectangularSymbolDependencyRecipe.SinglePortMemory =>
            [
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Address,
                    1,
                    "A",
                    "D",
                    "Q"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    2,
                    "CLK",
                    "D",
                    "WE"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.And,
                    3,
                    "WE",
                    "D"),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(recipe)),
        };
        return dependencies;
    }

    private static RectangularSymbolDependency[] Single(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        RectangularSymbolDependencyKind kind,
        uint identifier,
        string affectingPortId,
        params string[] affectedPortIds) =>
    [
        Dependency(ports, kind, identifier, affectingPortId, affectedPortIds),
    ];

    private static RectangularSymbolDependency Dependency(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        RectangularSymbolDependencyKind kind,
        uint identifier,
        string affectingPortId,
        params string[] affectedPortIds)
    {
        _ = ports.Single(port => port.Id == affectingPortId);
        var affectedEndpoints = affectedPortIds
            .Select((portId, index) =>
            {
                _ = ports.Single(port => port.Id == portId);
                return new RectangularSymbolAffectedEndpoint(portId, index);
            })
            .ToArray();
        return new RectangularSymbolDependency(
            kind,
            identifier,
            affectingPortId,
            affectedEndpoints);
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
