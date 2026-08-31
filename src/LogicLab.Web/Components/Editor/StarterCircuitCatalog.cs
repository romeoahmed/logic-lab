using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Components.Editor;

internal static class StarterCircuitCatalog
{
    private static StarterCircuitRecipe Inverter { get; } = new(
        "CircuitAuthored",
        [
            new(
                "input",
                "source.input",
                InputParameters(LogicValue.Zero),
                "ComponentInput",
                new GridPoint(0, 5)),
            new(
                "inverter",
                "logic.not",
                WidthParameters(1),
                "ComponentNot",
                new GridPoint(10, 0)),
            new(
                "output",
                "sink.output",
                OutputParameters(1),
                "ComponentOutput",
                new GridPoint(28, 5)),
        ],
        [],
        [
            new("input", "Q", "inverter", "A", [new(7, 7), new(11, 7)]),
            new("inverter", "Q", "output", "D", [new(26, 7), new(29, 7)]),
        ]);

    private static StarterCircuitRecipe Steering { get; } = new(
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

    private static StarterCircuitRecipe CarryLookahead { get; } = CreateCarryLookahead();

    private static StarterCircuitRecipe BitSerial { get; } = CreateBitSerial();

    public static ReadOnlyCollection<StarterExamplePlan> Examples { get; } =
        Array.AsReadOnly<StarterExamplePlan>(
        [
            new(
                StarterExample.Inverter,
                "author",
                "logic.not",
                "StarterInverterTitle",
                "StarterInverterDescription",
                Inverter),
            new(
                StarterExample.Steering,
                "author-steering",
                "logic.mux",
                "StarterSteeringTitle",
                "StarterSteeringDescription",
                Steering),
            new(
                StarterExample.CarryLookahead,
                "author-carry-lookahead",
                "logic.adder",
                "StarterCarryLookaheadTitle",
                "StarterCarryLookaheadDescription",
                CarryLookahead),
            new(
                StarterExample.BitSerial,
                "author-bit-serial",
                "sequential.shift_register",
                "StarterBitSerialTitle",
                "StarterBitSerialDescription",
                BitSerial),
        ]);

    public static StarterExamplePlan GetPlan(StarterExample example) =>
        Examples.FirstOrDefault(candidate => candidate.Example == example)
        ?? throw new ArgumentOutOfRangeException(nameof(example), example, null);

    private static StarterCircuitRecipe CreateCarryLookahead()
    {
        const int width = 4;
        List<StarterComponentPlan> components =
        [
            new(
                "inputA",
                "source.input",
                InputParameters(
                    LogicValue.One,
                    LogicValue.One,
                    LogicValue.Zero,
                    LogicValue.One),
                "ExampleInputA",
                new GridPoint(0, 0)),
            new(
                "inputB",
                "source.input",
                InputParameters(
                    LogicValue.Zero,
                    LogicValue.One,
                    LogicValue.One,
                    LogicValue.Zero),
                "ExampleInputB",
                new GridPoint(0, 10)),
            new(
                "carryIn",
                "source.input",
                InputParameters(LogicValue.Zero),
                "ExampleCarryIn",
                new GridPoint(0, 20)),
            new(
                "propagate",
                "logic.xor",
                GateParameters(width, 2),
                "ExamplePropagate",
                new GridPoint(18, 0)),
            new(
                "generate",
                "logic.and",
                GateParameters(width, 2),
                "ExampleGenerate",
                new GridPoint(18, 12)),
            new(
                "propagateBits",
                "topology.split",
                SplitParameters(width),
                "ExamplePropagateBits",
                new GridPoint(40, 0)),
            new(
                "generateBits",
                "topology.split",
                SplitParameters(width),
                "ExampleGenerateBits",
                new GridPoint(40, 14)),
        ];
        var connections = new List<StarterConnectionPlan>
        {
            new(
                "inputA",
                "Q",
                "propagate",
                "A0",
                Route(new GridPoint(7, 2), new GridPoint(19, 4), 12)),
            new(
                "inputB",
                "Q",
                "propagate",
                "A1",
                Route(new GridPoint(7, 12), new GridPoint(19, 8), 14)),
            new(
                "inputA",
                "Q",
                "generate",
                "A0",
                Route(new GridPoint(7, 2), new GridPoint(19, 16), 10)),
            new(
                "inputB",
                "Q",
                "generate",
                "A1",
                Route(new GridPoint(7, 12), new GridPoint(19, 20), 12)),
            new(
                "propagate",
                "Q",
                "propagateBits",
                "D",
                Route(new GridPoint(34, 6), new GridPoint(41, 6), 37)),
            new(
                "generate",
                "Q",
                "generateBits",
                "D",
                Route(new GridPoint(34, 18), new GridPoint(41, 20), 37)),
        };

        var carryAnchors = new List<GridPoint>();
        for (var carryIndex = 1; carryIndex <= width; carryIndex++)
        {
            var blockY = 30 + carryIndex * carryIndex * 6;
            var carryKey = $"carry{carryIndex}";
            components.Add(new StarterComponentPlan(
                carryKey,
                "logic.or",
                GateParameters(1, carryIndex + 1),
                $"ExampleCarry{carryIndex}",
                new GridPoint(92, blockY)));

            var carryInput = new GridPoint(93, blockY + 4);
            connections.Add(new StarterConnectionPlan(
                "generateBits",
                $"Q{carryIndex - 1}",
                carryKey,
                "A0",
                Route(
                    new GridPoint(56, 18 + (carryIndex - 1) * 4),
                    carryInput,
                    62 + carryIndex)));

            for (var termIndex = 1; termIndex <= carryIndex; termIndex++)
            {
                var termKey = $"carry{carryIndex}Term{termIndex}";
                var termY = blockY + termIndex * 10;
                components.Add(new StarterComponentPlan(
                    termKey,
                    "logic.and",
                    GateParameters(1, termIndex + 1),
                    "ExampleCarryTerm",
                    new GridPoint(68, termY)));

                for (var propagateIndex = 0;
                     propagateIndex < termIndex;
                     propagateIndex++)
                {
                    var bitIndex = carryIndex - 1 - propagateIndex;
                    connections.Add(new StarterConnectionPlan(
                        "propagateBits",
                        $"Q{bitIndex}",
                        termKey,
                        $"A{propagateIndex}",
                        Route(
                            new GridPoint(56, 4 + bitIndex * 4),
                            new GridPoint(69, termY + 2 + propagateIndex * 2),
                            59 + propagateIndex)));
                }

                var finalInputPort = $"A{termIndex}";
                if (termIndex == carryIndex)
                {
                    connections.Add(new StarterConnectionPlan(
                        "carryIn",
                        "Q",
                        termKey,
                        finalInputPort,
                        Route(
                            new GridPoint(7, 22),
                            new GridPoint(69, termY + 2 + termIndex * 2),
                            64)));
                }
                else
                {
                    var generateIndex = carryIndex - termIndex - 1;
                    connections.Add(new StarterConnectionPlan(
                        "generateBits",
                        $"Q{generateIndex}",
                        termKey,
                        finalInputPort,
                        Route(
                            new GridPoint(56, 18 + generateIndex * 4),
                            new GridPoint(69, termY + 2 + termIndex * 2),
                            65)));
                }

                connections.Add(new StarterConnectionPlan(
                    termKey,
                    "Q",
                    carryKey,
                    $"A{termIndex}",
                    Route(
                        new GridPoint(84, termY + 5),
                        new GridPoint(93, blockY + 4 + termIndex * 2),
                        88)));
            }

            carryAnchors.Add(new GridPoint(108, blockY + 5));
        }

        var finalCarryAnchor = carryAnchors[^1];

        components.AddRange(
        [
            new(
                "carryVector",
                "topology.concat",
                ConcatParameters(width),
                "ExampleCarryVector",
                new GridPoint(116, 4)),
            new(
                "sumGate",
                "logic.xor",
                GateParameters(width, 2),
                "ExampleSum",
                new GridPoint(136, 4)),
            new(
                "sum",
                "sink.output",
                OutputParameters(width),
                "ExampleSum",
                new GridPoint(158, 8)),
            new(
                "carryOut",
                "sink.output",
                OutputParameters(1),
                "ExampleCarryOut",
                new GridPoint(116, finalCarryAnchor.Y - 2)),
        ]);

        connections.Add(new StarterConnectionPlan(
            "carryIn",
            "Q",
            "carryVector",
            "D0",
            Route(new GridPoint(7, 22), new GridPoint(117, 8), 112)));
        for (var carryIndex = 1; carryIndex < width; carryIndex++)
        {
            connections.Add(new StarterConnectionPlan(
                $"carry{carryIndex}",
                "Q",
                "carryVector",
                $"D{carryIndex}",
                Route(
                    carryAnchors[carryIndex - 1],
                    new GridPoint(117, 8 + carryIndex * 3),
                    112 + carryIndex)));
        }

        connections.AddRange(
        [
            new(
                "propagate",
                "Q",
                "sumGate",
                "A0",
                Route(new GridPoint(34, 6), new GridPoint(137, 8), 130)),
            new(
                "carryVector",
                "Q",
                "sumGate",
                "A1",
                Route(new GridPoint(130, 12), new GridPoint(137, 12), 133)),
            new(
                "sumGate",
                "Q",
                "sum",
                "D",
                Route(new GridPoint(152, 10), new GridPoint(159, 10), 155)),
            new(
                "carry4",
                "Q",
                "carryOut",
                "D",
                Route(finalCarryAnchor, new GridPoint(117, finalCarryAnchor.Y), 112)),
        ]);

        return new StarterCircuitRecipe(
            "CarryLookaheadExampleAuthored",
            components,
            [
                new("P = A XOR B", new GridPoint(18, 28)),
                new("G = A AND B", new GridPoint(40, 28)),
            ],
            connections);
    }

    private static StarterCircuitRecipe CreateBitSerial() => new(
        "BitSerialExampleAuthored",
        [
            new(
                "inputA",
                "source.input",
                InputParameters(
                    LogicValue.One,
                    LogicValue.One,
                    LogicValue.Zero,
                    LogicValue.One),
                "ExampleInputA",
                new GridPoint(0, 0)),
            new(
                "inputB",
                "source.input",
                InputParameters(
                    LogicValue.Zero,
                    LogicValue.One,
                    LogicValue.One,
                    LogicValue.Zero),
                "ExampleInputB",
                new GridPoint(0, 12)),
            new(
                "load",
                "source.input",
                InputParameters(LogicValue.Zero),
                "ExampleLoad",
                new GridPoint(0, 24)),
            new(
                "clock",
                "source.clock",
                ClockParameters(),
                "ExampleClock",
                new GridPoint(0, 34)),
            new(
                "serialZero",
                "source.constant",
                ConstantParameters(LogicValue.Zero),
                "ExampleSerialZero",
                new GridPoint(0, 44)),
            new(
                "parallelZero",
                "source.constant",
                ConstantParameters(
                    LogicValue.Zero,
                    LogicValue.Zero,
                    LogicValue.Zero,
                    LogicValue.Zero),
                "ExampleParallelZero",
                new GridPoint(0, 54)),
            new(
                "enable",
                "source.constant",
                ConstantParameters(LogicValue.One),
                "ExampleEnable",
                new GridPoint(0, 64)),
            new(
                "registerA",
                "sequential.shift_register",
                ShiftRegisterParameters(
                    LogicValue.One,
                    LogicValue.One,
                    LogicValue.Zero,
                    LogicValue.One),
                "ExampleOperandARegister",
                new GridPoint(24, 0)),
            new(
                "registerB",
                "sequential.shift_register",
                ShiftRegisterParameters(
                    LogicValue.Zero,
                    LogicValue.One,
                    LogicValue.One,
                    LogicValue.Zero),
                "ExampleOperandBRegister",
                new GridPoint(24, 16)),
            new(
                "serialAdder",
                "logic.adder",
                WidthParameters(1),
                "ExampleSerialAdder",
                new GridPoint(52, 8)),
            new(
                "carryRegister",
                "sequential.shift_register",
                ShiftRegisterParameters(LogicValue.Zero),
                "ExampleCarryRegister",
                new GridPoint(52, 26)),
            new(
                "resultRegister",
                "sequential.shift_register",
                ShiftRegisterParameters(
                    LogicValue.Zero,
                    LogicValue.Zero,
                    LogicValue.Zero,
                    LogicValue.Zero),
                "ExampleResultRegister",
                new GridPoint(78, 8)),
            new(
                "result",
                "sink.output",
                OutputParameters(4),
                "ExampleResult",
                new GridPoint(108, 10)),
            new(
                "carryOut",
                "sink.output",
                OutputParameters(1),
                "ExampleCarryOut",
                new GridPoint(108, 30)),
        ],
        [
            new("LSB first", new GridPoint(42, 4)),
            new("Σ", new GridPoint(72, 10)),
        ],
        [
            new("inputA", "Q", "registerA", "PARALLEL", Route(new(7, 2), new(25, 4), 16)),
            new("inputB", "Q", "registerB", "PARALLEL", Route(new(7, 14), new(25, 20), 17)),
            new("serialZero", "Q", "registerA", "SERIAL", Route(new(7, 46), new(25, 6), 19)),
            new("serialZero", "Q", "registerB", "SERIAL", Route(new(7, 46), new(25, 22), 20)),
            new("load", "Q", "registerA", "LOAD", Route(new(7, 26), new(25, 8), 21)),
            new("load", "Q", "registerB", "LOAD", Route(new(7, 26), new(25, 24), 22)),
            new("load", "Q", "resultRegister", "LOAD", Route(new(7, 26), new(79, 16), 74)),
            new("clock", "Q", "registerA", "CLK", Route(new(7, 36), new(25, 10), 23)),
            new("clock", "Q", "registerB", "CLK", Route(new(7, 36), new(25, 26), 24)),
            new("clock", "Q", "carryRegister", "CLK", Route(new(7, 36), new(53, 36), 48)),
            new("clock", "Q", "resultRegister", "CLK", Route(new(7, 36), new(79, 18), 76)),
            new("enable", "Q", "registerA", "EN", Route(new(7, 66), new(25, 12), 18)),
            new("enable", "Q", "registerB", "EN", Route(new(7, 66), new(25, 28), 19)),
            new("enable", "Q", "carryRegister", "EN", Route(new(7, 66), new(53, 38), 50)),
            new("enable", "Q", "resultRegister", "EN", Route(new(7, 66), new(79, 20), 75)),
            new("parallelZero", "Q", "resultRegister", "PARALLEL", Route(new(7, 56), new(79, 12), 72)),
            new("serialZero", "Q", "carryRegister", "PARALLEL", Route(new(7, 46), new(53, 30), 47)),
            new("load", "Q", "carryRegister", "LOAD", Route(new(7, 26), new(53, 34), 46)),
            new("registerA", "SERIAL_OUT", "serialAdder", "A", Route(new(44, 8), new(53, 11), 48)),
            new("registerB", "SERIAL_OUT", "serialAdder", "B", Route(new(44, 24), new(53, 13), 49)),
            new("carryRegister", "Q", "serialAdder", "CIN", Route(new(72, 32), new(53, 15), 76)),
            new("serialAdder", "SUM", "resultRegister", "SERIAL", Route(new(67, 12), new(79, 14), 73)),
            new("serialAdder", "COUT", "carryRegister", "SERIAL", Route(new(67, 14), new(53, 32), 72)),
            new("resultRegister", "Q", "result", "D", Route(new(98, 14), new(109, 12), 103)),
            new("carryRegister", "Q", "carryOut", "D", Route(new(72, 32), new(109, 32), 102)),
        ]);

    private static ComponentParameterBinding[] InputParameters(
        params LogicValue[] initialValue) =>
    [
        new(
            "width",
            new Unsigned32ParameterValue(checked((uint)initialValue.Length))),
        new("initialValue", new LogicVectorParameterValue(initialValue)),
    ];

    private static ComponentParameterBinding[] OutputParameters(int width) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)width))),
        new("radix", new ChoiceParameterValue("binary")),
    ];

    private static ComponentParameterBinding[] WidthParameters(int width) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)width))),
    ];

    private static ComponentParameterBinding[] ConstantParameters(
        params LogicValue[] value) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)value.Length))),
        new("value", new LogicVectorParameterValue(value)),
    ];

    private static ComponentParameterBinding[] GateParameters(int width, int fanIn) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)width))),
        new("fanIn", new Unsigned32ParameterValue(checked((uint)fanIn))),
    ];

    private static ComponentParameterBinding[] SplitParameters(int width) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)width))),
        new(
            "slices",
            new SlicesParameterValue(
                [.. Enumerable.Range(0, width)
                    .Select(index => new BitSlice(checked((uint)index), 1))])),
    ];

    private static ComponentParameterBinding[] ConcatParameters(int width) =>
    [
        new(
            "inputWidths",
            new WidthsParameterValue(
                [.. Enumerable.Repeat(1u, width)])),
    ];

    private static ComponentParameterBinding[] ClockParameters() =>
    [
        new("initialValue", new LogicVectorParameterValue([LogicValue.Zero])),
        new("firstTransition", new Unsigned64ParameterValue(1)),
        new("highDuration", new Unsigned64ParameterValue(1)),
        new("lowDuration", new Unsigned64ParameterValue(1)),
    ];

    private static ComponentParameterBinding[] ShiftRegisterParameters(
        params LogicValue[] initialState) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)initialState.Length))),
        new("direction", new ChoiceParameterValue("towardLow")),
        new("edge", new ChoiceParameterValue("rising")),
        new("initialState", new LogicVectorParameterValue(initialState)),
    ];

    private static GridPoint[] Route(GridPoint source, GridPoint destination, int laneX)
    {
        if (source.Y == destination.Y)
        {
            return [source, destination];
        }

        return
        [
            source,
            new GridPoint(laneX, source.Y),
            new GridPoint(laneX, destination.Y),
            destination,
        ];
    }
}

internal sealed record StarterExamplePlan(
    StarterExample Example,
    string Command,
    string SymbolContractId,
    string TitleResourceKey,
    string DescriptionResourceKey,
    StarterCircuitRecipe Recipe);

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
