using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

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
    RectangularSymbolInputFunctionKind? InputFunctionKind = null,
    bool IsComplemented = false);

internal enum RectangularSymbolInputFunctionKind
{
    Shift,
    Count,
}

internal readonly record struct RectangularSymbolDependencyIdentifierRange
{
    public RectangularSymbolDependencyIdentifierRange(uint first, uint last)
    {
        if (last < first)
        {
            throw new ArgumentOutOfRangeException(
                nameof(last),
                last,
                "A dependency identifier range cannot be descending.");
        }

        First = first;
        Last = last;
    }

    public uint First { get; }

    public uint Last { get; }

    public static RectangularSymbolDependencyIdentifierRange Single(uint identifier) =>
        new(identifier, identifier);
}

internal sealed record RectangularSymbolDependency(
    RectangularSymbolDependencyKind Kind,
    RectangularSymbolDependencyIdentifierRange IdentifierRange,
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
                            RectangularSymbolInputFunctionKind.Shift,
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
                            RectangularSymbolInputFunctionKind.Shift,
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
                            RectangularSymbolInputFunctionKind.Count,
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
                            RectangularSymbolInputFunctionKind.Count,
                            1),
                    ]),
            ],
            RectangularSymbolDependencyRecipe.ReadOnlyMemory =>
                AddressDependencies(
                    ports,
                    [AffectedPort(ports, "Q", 0)]),
            RectangularSymbolDependencyRecipe.SinglePortMemory =>
            [
                .. AddressDependencies(
                    ports,
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
            RectangularSymbolDependencyIdentifierRange.Single(identifier),
            affectingPortId,
            affectedEndpoints);
    }

    private static RectangularSymbolDependency[] AddressDependencies(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        RectangularSymbolAffectedEndpoint[] affectedEndpoints)
    {
        var address = ports.Single(port =>
            port.Id == "A" && port.Direction == PortDirection.Input);
        return address.Width switch
        {
            0 => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
            1 =>
            [
                new RectangularSymbolDependency(
                    RectangularSymbolDependencyKind.Address,
                    new RectangularSymbolDependencyIdentifierRange(0, 1),
                    address.Id,
                    affectedEndpoints),
            ],
            _ => [],
        };
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
            InputFunctionKind: null,
            IsComplemented: isComplemented);
    }

    private static RectangularSymbolAffectedEndpoint AffectedInputFunction(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string portId,
        RectangularSymbolInputFunctionKind inputFunctionKind,
        int applicationOrder,
        bool isComplemented = false)
    {
        _ = ports.Single(port =>
            port.Id == portId && port.Direction == PortDirection.Input);
        return new RectangularSymbolAffectedEndpoint(
            portId,
            applicationOrder,
            inputFunctionKind,
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
                RectangularSymbolDependencyIdentifierRange.Single(1),
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
                RectangularSymbolDependencyIdentifierRange.Single(checked((uint)index)),
                selectorPortId,
                [new RectangularSymbolAffectedEndpoint(affected[index].Id, 0)]);
        }

        return dependencies;
    }
}
