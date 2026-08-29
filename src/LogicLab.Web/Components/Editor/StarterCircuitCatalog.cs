using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Components.Editor;

internal static class StarterCircuitCatalog
{
    public static StarterCircuitRecipe Steering { get; } = new(
        "SteeringExampleAuthored",
        [
            new(
                "data0",
                "source.input",
                InputParameters(LogicValue.Zero),
                "ExampleData0",
                new GridPoint(0, 0)),
            new(
                "data1",
                "source.input",
                InputParameters(LogicValue.One),
                "ExampleData1",
                new GridPoint(0, 7)),
            new(
                "select",
                "source.input",
                InputParameters(LogicValue.Zero),
                "ExampleSelect",
                new GridPoint(0, 14)),
            new(
                "mux",
                "logic.mux",
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "selectorWidth",
                        new Unsigned32ParameterValue(1)),
                ],
                "ExampleMultiplexer",
                new GridPoint(20, 5)),
            new(
                "output",
                "sink.output",
                OutputParameters(1),
                "ExampleSelectedOutput",
                new GridPoint(42, 8)),
        ],
        [
            new("D0", new GridPoint(9, 1)),
            new("D1", new GridPoint(9, 8)),
            new("S", new GridPoint(9, 15)),
        ],
        [
            new(
                "data0",
                "Q",
                "mux",
                "D0",
                [new(7, 2), new(14, 2), new(14, 8), new(21, 8)]),
            new(
                "data1",
                "Q",
                "mux",
                "D1",
                [new(7, 9), new(15, 9), new(15, 10), new(21, 10)]),
            new(
                "select",
                "Q",
                "mux",
                "S",
                [new(7, 16), new(16, 16), new(16, 12), new(21, 12)]),
            new("mux", "Q", "output", "D", [new(37, 10), new(43, 10)]),
        ]);

    public static StarterCircuitRecipe Arithmetic { get; } = new(
        "ArithmeticExampleAuthored",
        [
            new(
                "inputA",
                "source.input",
                InputParameters(LogicValue.One, LogicValue.Zero, LogicValue.One),
                "ExampleInputA",
                new GridPoint(0, 0)),
            new(
                "inputB",
                "source.input",
                InputParameters(LogicValue.One, LogicValue.One, LogicValue.Zero),
                "ExampleInputB",
                new GridPoint(0, 7)),
            new(
                "carryIn",
                "source.input",
                InputParameters(LogicValue.Zero),
                "ExampleCarryIn",
                new GridPoint(0, 14)),
            new(
                "adder",
                "logic.adder",
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(3))],
                "ExampleAdder",
                new GridPoint(20, 5)),
            new(
                "sum",
                "sink.output",
                OutputParameters(3),
                "ExampleSum",
                new GridPoint(42, 7)),
            new(
                "carryOut",
                "sink.output",
                OutputParameters(1),
                "ExampleCarryOut",
                new GridPoint(42, 14)),
        ],
        [
            new("A", new GridPoint(9, 1)),
            new("B", new GridPoint(9, 8)),
            new("CIN", new GridPoint(9, 15)),
        ],
        [
            new(
                "inputA",
                "Q",
                "adder",
                "A",
                [new(7, 2), new(14, 2), new(14, 8), new(21, 8)]),
            new(
                "inputB",
                "Q",
                "adder",
                "B",
                [new(7, 9), new(15, 9), new(15, 10), new(21, 10)]),
            new(
                "carryIn",
                "Q",
                "adder",
                "CIN",
                [new(7, 16), new(16, 16), new(16, 12), new(21, 12)]),
            new("adder", "SUM", "sum", "D", [new(35, 9), new(43, 9)]),
            new(
                "adder",
                "COUT",
                "carryOut",
                "D",
                [new(35, 11), new(39, 11), new(39, 16), new(43, 16)]),
        ]);

    private static ComponentParameterBinding[] InputParameters(
        params LogicValue[] initialValue) =>
    [
        new(
            "width",
            new Unsigned32ParameterValue(checked((uint)initialValue.Length))),
        new("initialValue", new LogicVectorParameterValue(initialValue)),
    ];

    private static ComponentParameterBinding[] OutputParameters(uint width) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("radix", new ChoiceParameterValue("binary")),
    ];
}

internal sealed class StarterCircuitRecipe
{
    public StarterCircuitRecipe(
        string statusResourceKey,
        IReadOnlyList<StarterComponentPlan> components,
        IReadOnlyList<StarterAnnotationPlan> annotations,
        IReadOnlyList<StarterConnectionPlan> connections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusResourceKey);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(connections);
        var ownedComponents = components.ToArray();
        var componentKeys = ownedComponents
            .Select(component => component.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (ownedComponents.Length == 0
            || componentKeys.Count != ownedComponents.Length
            || connections.Any(connection =>
                !componentKeys.Contains(connection.SourceKey)
                || !componentKeys.Contains(connection.DestinationKey)))
        {
            throw new ArgumentException("The starter circuit recipe is inconsistent.");
        }

        StatusResourceKey = statusResourceKey;
        Components = Array.AsReadOnly(ownedComponents);
        Annotations = Array.AsReadOnly(annotations.ToArray());
        Connections = Array.AsReadOnly(connections.ToArray());
    }

    public string StatusResourceKey { get; }

    public ReadOnlyCollection<StarterComponentPlan> Components { get; }

    public ReadOnlyCollection<StarterAnnotationPlan> Annotations { get; }

    public ReadOnlyCollection<StarterConnectionPlan> Connections { get; }
}

internal sealed record StarterComponentPlan
{
    public StarterComponentPlan(
        string key,
        string contractId,
        IReadOnlyList<ComponentParameterBinding> parameters,
        string displayNameResourceKey,
        GridPoint origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayNameResourceKey);
        Key = key;
        ContractId = contractId;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        DisplayNameResourceKey = displayNameResourceKey;
        Origin = origin;
    }

    public string Key { get; }

    public string ContractId { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public string DisplayNameResourceKey { get; }

    public GridPoint Origin { get; }
}

internal sealed record StarterAnnotationPlan(string Text, GridPoint Position);

internal sealed record StarterConnectionPlan
{
    public StarterConnectionPlan(
        string sourceKey,
        string sourcePortId,
        string destinationKey,
        string destinationPortId,
        IReadOnlyList<GridPoint> route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePortId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPortId);
        SourceKey = sourceKey;
        SourcePortId = sourcePortId;
        DestinationKey = destinationKey;
        DestinationPortId = destinationPortId;
        Route = new OrthogonalWireRoute(route);
    }

    public string SourceKey { get; }

    public string SourcePortId { get; }

    public string DestinationKey { get; }

    public string DestinationPortId { get; }

    public OrthogonalWireRoute Route { get; }
}
