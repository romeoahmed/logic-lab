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
    TransparentLatch,
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
    int ApplicationOrder,
    string? InputFunctionQualifierId = null,
    bool IsComplemented = false);

internal static class RectangularSymbolInputFunctionQualifierIds
{
    public const string Shift = "shift";
    public const string Count = "count";
}

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
            RectangularSymbolDependencyRecipe.TransparentLatch =>
                Single(ports, RectangularSymbolDependencyKind.Control, 1, "EN", "D"),
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
                    0,
                    "D"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Enable,
                    2,
                    "EN",
                    1,
                    "D"),
            ],
            RectangularSymbolDependencyRecipe.ShiftRegister =>
            [
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Mode,
                    1,
                    "LOAD",
                    [
                        AffectedPort(ports, "PARALLEL", 0),
                        AffectedPort(ports, "SERIAL", 0, isComplemented: true),
                        AffectedInputFunction(
                            ports,
                            "CLK",
                            RectangularSymbolInputFunctionQualifierIds.Shift,
                            0,
                            isComplemented: true),
                    ]),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    2,
                    "CLK",
                    [
                        AffectedPort(ports, "PARALLEL", 1),
                        AffectedPort(ports, "SERIAL", 1),
                    ]),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Enable,
                    3,
                    "EN",
                    [
                        AffectedPort(ports, "SERIAL", 2),
                        AffectedInputFunction(
                            ports,
                            "CLK",
                            RectangularSymbolInputFunctionQualifierIds.Shift,
                            1),
                    ]),
            ],
            RectangularSymbolDependencyRecipe.Counter =>
            [
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Mode,
                    1,
                    "LOAD",
                    [
                        AffectedPort(ports, "LOAD_VALUE", 0),
                        AffectedInputFunction(
                            ports,
                            "CLK",
                            RectangularSymbolInputFunctionQualifierIds.Count,
                            0,
                            isComplemented: true),
                    ]),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    2,
                    "CLK",
                    1,
                    "LOAD_VALUE"),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Enable,
                    3,
                    "EN",
                    [
                        AffectedInputFunction(
                            ports,
                            "CLK",
                            RectangularSymbolInputFunctionQualifierIds.Count,
                            1),
                    ]),
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
                    [
                        AffectedPort(ports, "D", 0),
                        AffectedPort(ports, "Q", 0),
                    ]),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Control,
                    2,
                    "CLK",
                    [
                        AffectedPort(ports, "D", 1),
                        AffectedPort(ports, "WE", 0),
                    ]),
                Dependency(
                    ports,
                    RectangularSymbolDependencyKind.Enable,
                    3,
                    "WE",
                    2,
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
        Dependency(ports, kind, identifier, affectingPortId, 0, affectedPortIds),
    ];

    private static RectangularSymbolDependency Dependency(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        RectangularSymbolDependencyKind kind,
        uint identifier,
        string affectingPortId,
        int applicationOrder,
        params string[] affectedPortIds)
    {
        var affectedEndpoints = affectedPortIds
            .Select(portId => AffectedPort(ports, portId, applicationOrder))
            .ToArray();
        return Dependency(ports, kind, identifier, affectingPortId, affectedEndpoints);
    }

    private static RectangularSymbolDependency Dependency(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        RectangularSymbolDependencyKind kind,
        uint identifier,
        string affectingPortId,
        RectangularSymbolAffectedEndpoint[] affectedEndpoints)
    {
        _ = ports.Single(port => port.Id == affectingPortId);
        return new RectangularSymbolDependency(
            kind,
            identifier,
            affectingPortId,
            affectedEndpoints);
    }

    private static RectangularSymbolAffectedEndpoint AffectedPort(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string portId,
        int applicationOrder,
        bool isComplemented = false)
    {
        _ = ports.Single(port => port.Id == portId);
        return new RectangularSymbolAffectedEndpoint(
            portId,
            applicationOrder,
            InputFunctionQualifierId: null,
            IsComplemented: isComplemented);
    }

    private static RectangularSymbolAffectedEndpoint AffectedInputFunction(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string portId,
        string inputFunctionQualifierId,
        int applicationOrder,
        bool isComplemented = false)
    {
        _ = ports.Single(port =>
            port.Id == portId && port.Direction == PortDirection.Input);
        return new RectangularSymbolAffectedEndpoint(
            portId,
            applicationOrder,
            inputFunctionQualifierId,
            isComplemented);
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
