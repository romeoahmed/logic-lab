using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Benchmarks;

internal static class BenchmarkCircuitCatalog
{
    public static AuthoredBenchmarkCircuit Create(
        CircuitBenchmarkCase benchmarkCase) => benchmarkCase.Shape switch
        {
            CircuitBenchmarkShape.FlatAndChain => FlatAndChain(benchmarkCase.Size),
            CircuitBenchmarkShape.HierarchicalInverterChain =>
                HierarchicalInverterChain(benchmarkCase.Size),
            CircuitBenchmarkShape.InverterFeedbackBank =>
                InverterFeedbackBank(benchmarkCase.Size),
            CircuitBenchmarkShape.DFlipFlopBank => DFlipFlopBank(benchmarkCase.Size),
            CircuitBenchmarkShape.SinglePortRam => SinglePortRam(benchmarkCase.Size),
            _ => throw new InvalidOperationException("Unknown benchmark circuit shape."),
        };

    private static AuthoredBenchmarkCircuit FlatAndChain(int gateCount)
    {
        var builder = BenchmarkCircuitBuilder.Create();
        var definitionId = builder.EntryDefinitionId;
        var source = builder.PlaceLibrary(
            definitionId,
            "source.input",
            InputParameters(LogicValue.Zero),
            new GridPoint(0, 0));
        var previous = source;
        for (var index = 0; index < gateCount; index++)
        {
            var gate = builder.PlaceLibrary(
                definitionId,
                "logic.and",
                GateParameters(),
                new GridPoint(checked((index + 1) * 4), 0));
            _ = builder.Connect(
                definitionId,
                BenchmarkCircuitBuilder.Port(definitionId, previous, "Q"),
                BenchmarkCircuitBuilder.Port(definitionId, gate, "A0"),
                BenchmarkCircuitBuilder.Port(definitionId, gate, "A1"));
            previous = gate;
        }

        var sink = builder.PlaceLibrary(
            definitionId,
            "sink.output",
            SinkParameters(),
            new GridPoint(checked((gateCount + 1) * 4), 0));
        var output = builder.Connect(
            definitionId,
            BenchmarkCircuitBuilder.Port(definitionId, previous, "Q"),
            BenchmarkCircuitBuilder.Port(definitionId, sink, "D"));
        return new AuthoredBenchmarkCircuit(builder.Revision, [output], source);
    }

    private static AuthoredBenchmarkCircuit HierarchicalInverterChain(
        int instanceCount)
    {
        var builder = BenchmarkCircuitBuilder.Create();
        var entryId = builder.EntryDefinitionId;
        var child = builder.CreateDefinition(
            "Inverter",
            new DefinitionPortDeclaration(
                "A",
                PortDirection.Input,
                1,
                new DefinitionPortPlacement(
                    new GridPoint(0, 2),
                    CardinalDirection.West)),
            new DefinitionPortDeclaration(
                "Q",
                PortDirection.Output,
                1,
                new DefinitionPortPlacement(
                    new GridPoint(8, 2),
                    CardinalDirection.East)));
        var inputPort = child.Ports.Single(
            static port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(
            static port => port.Direction == PortDirection.Output);
        var inverter = builder.PlaceLibrary(
            child.Id,
            "logic.not",
            WidthParameters(),
            new GridPoint(4, 2));
        _ = builder.Connect(
            child.Id,
            new DefinitionTerminalReference(child.Id, inputPort.Id),
            BenchmarkCircuitBuilder.Port(child.Id, inverter, "A"));
        _ = builder.Connect(
            child.Id,
            BenchmarkCircuitBuilder.Port(child.Id, inverter, "Q"),
            new DefinitionTerminalReference(child.Id, outputPort.Id));

        var source = builder.PlaceLibrary(
            entryId,
            "source.input",
            InputParameters(LogicValue.Zero),
            new GridPoint(0, 0));
        AuthoredTerminalReference driving = BenchmarkCircuitBuilder.Port(
            entryId,
            source,
            "Q");
        for (var index = 0; index < instanceCount; index++)
        {
            var instance = builder.PlaceDefinition(
                entryId,
                child.Id,
                new GridPoint(checked((index + 1) * 4), 0),
                $"Inverter {index + 1}");
            _ = builder.Connect(
                entryId,
                driving,
                BenchmarkCircuitBuilder.Port(
                    entryId,
                    instance,
                    inputPort.Id.Value));
            driving = BenchmarkCircuitBuilder.Port(
                entryId,
                instance,
                outputPort.Id.Value);
        }

        var sink = builder.PlaceLibrary(
            entryId,
            "sink.output",
            SinkParameters(),
            new GridPoint(checked((instanceCount + 1) * 4), 0));
        var output = builder.Connect(
            entryId,
            driving,
            BenchmarkCircuitBuilder.Port(entryId, sink, "D"));
        return new AuthoredBenchmarkCircuit(builder.Revision, [output], source);
    }

    private static AuthoredBenchmarkCircuit InverterFeedbackBank(int ringCount)
    {
        var builder = BenchmarkCircuitBuilder.Create();
        var definitionId = builder.EntryDefinitionId;
        var outputs = new Net[ringCount];
        for (var index = 0; index < ringCount; index++)
        {
            var y = checked(index * 4);
            var inverter = builder.PlaceLibrary(
                definitionId,
                "logic.not",
                WidthParameters(),
                new GridPoint(0, y));
            var sink = builder.PlaceLibrary(
                definitionId,
                "sink.output",
                SinkParameters(),
                new GridPoint(4, y));
            outputs[index] = builder.Connect(
                definitionId,
                BenchmarkCircuitBuilder.Port(definitionId, inverter, "Q"),
                BenchmarkCircuitBuilder.Port(definitionId, inverter, "A"),
                BenchmarkCircuitBuilder.Port(definitionId, sink, "D"));
        }

        return new AuthoredBenchmarkCircuit(builder.Revision, outputs, null);
    }

    private static AuthoredBenchmarkCircuit DFlipFlopBank(int registerCount)
    {
        var builder = BenchmarkCircuitBuilder.Create();
        var definitionId = builder.EntryDefinitionId;
        var data = builder.PlaceLibrary(
            definitionId,
            "source.input",
            InputParameters(LogicValue.One),
            new GridPoint(0, 0));
        var clock = builder.PlaceLibrary(
            definitionId,
            "source.clock",
            ClockParameters(),
            new GridPoint(0, 4));
        var registers = new ComponentInstance[registerCount];
        var sinks = new ComponentInstance[registerCount];
        for (var index = 0; index < registerCount; index++)
        {
            var y = checked(index * 4);
            registers[index] = builder.PlaceLibrary(
                definitionId,
                "sequential.dff",
                DffParameters(),
                new GridPoint(4, y));
            sinks[index] = builder.PlaceLibrary(
                definitionId,
                "sink.output",
                SinkParameters(),
                new GridPoint(8, y));
        }

        _ = builder.Connect(
            definitionId,
            [
                BenchmarkCircuitBuilder.Port(definitionId, data, "Q"),
                .. registers.Select(register =>
                    BenchmarkCircuitBuilder.Port(definitionId, register, "D")),
            ]);
        _ = builder.Connect(
            definitionId,
            [
                BenchmarkCircuitBuilder.Port(definitionId, clock, "Q"),
                .. registers.Select(register =>
                    BenchmarkCircuitBuilder.Port(definitionId, register, "CLK")),
            ]);
        var outputs = new Net[registerCount];
        for (var index = 0; index < registerCount; index++)
        {
            outputs[index] = builder.Connect(
                definitionId,
                BenchmarkCircuitBuilder.Port(definitionId, registers[index], "Q"),
                BenchmarkCircuitBuilder.Port(definitionId, sinks[index], "D"));
        }

        return new AuthoredBenchmarkCircuit(builder.Revision, outputs, null);
    }

    private static AuthoredBenchmarkCircuit SinglePortRam(int depth)
    {
        const int wordWidth = 8;
        var builder = BenchmarkCircuitBuilder.Create();
        var definitionId = builder.EntryDefinitionId;
        MemoryImageWord[] words =
        [
            .. Enumerable.Range(0, depth).Select(static value => new MemoryImageWord(
                [
                    .. Enumerable.Range(0, wordWidth).Select(bit =>
                        ((value >> bit) & 1) == 0
                            ? LogicValue.Zero
                            : LogicValue.One),
                ])),
        ];
        var image = builder.CreateMemoryImage("RAM corpus", wordWidth, words);
        var addressWidth = checked((uint)System.Numerics.BitOperations.Log2(
            checked((uint)depth)));
        if ((1U << checked((int)addressWidth)) != depth)
        {
            throw new InvalidOperationException(
                "The RAM benchmark depth must be a power of two.");
        }

        var address = builder.PlaceLibrary(
            definitionId,
            "source.input",
            InputParameters(LogicValue.Zero, addressWidth),
            new GridPoint(0, 0));
        var data = builder.PlaceLibrary(
            definitionId,
            "source.input",
            InputVectorParameters(
                [
                    LogicValue.One,
                    LogicValue.Zero,
                    LogicValue.One,
                    LogicValue.Zero,
                    LogicValue.One,
                    LogicValue.Zero,
                    LogicValue.One,
                    LogicValue.Zero,
                ]),
            new GridPoint(0, 4));
        var writeEnable = builder.PlaceLibrary(
            definitionId,
            "source.input",
            InputParameters(LogicValue.One),
            new GridPoint(0, 8));
        var clock = builder.PlaceLibrary(
            definitionId,
            "source.clock",
            ClockParameters(),
            new GridPoint(0, 12));
        var ram = builder.PlaceLibrary(
            definitionId,
            "memory.ram_single_port",
            [
                new("addressWidth", new Unsigned32ParameterValue(addressWidth)),
                new("wordWidth", new Unsigned32ParameterValue(wordWidth)),
                new("initialImage", new MemoryImageParameterValue(image.Id)),
            ],
            new GridPoint(4, 4));
        var sink = builder.PlaceLibrary(
            definitionId,
            "sink.output",
            SinkParameters(wordWidth),
            new GridPoint(8, 4));
        _ = builder.Connect(
            definitionId,
            BenchmarkCircuitBuilder.Port(definitionId, address, "Q"),
            BenchmarkCircuitBuilder.Port(definitionId, ram, "A"));
        _ = builder.Connect(
            definitionId,
            BenchmarkCircuitBuilder.Port(definitionId, data, "Q"),
            BenchmarkCircuitBuilder.Port(definitionId, ram, "D"));
        _ = builder.Connect(
            definitionId,
            BenchmarkCircuitBuilder.Port(definitionId, writeEnable, "Q"),
            BenchmarkCircuitBuilder.Port(definitionId, ram, "WE"));
        _ = builder.Connect(
            definitionId,
            BenchmarkCircuitBuilder.Port(definitionId, clock, "Q"),
            BenchmarkCircuitBuilder.Port(definitionId, ram, "CLK"));
        var output = builder.Connect(
            definitionId,
            BenchmarkCircuitBuilder.Port(definitionId, ram, "Q"),
            BenchmarkCircuitBuilder.Port(definitionId, sink, "D"));
        return new AuthoredBenchmarkCircuit(builder.Revision, [output], null);
    }

    private static ComponentParameterBinding[] WidthParameters(uint width = 1) =>
        [new("width", new Unsigned32ParameterValue(width))];

    private static ComponentParameterBinding[] InputParameters(
        LogicValue value,
        uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("initialValue", new LogicVectorParameterValue(
            [.. Enumerable.Repeat(value, checked((int)width))])),
    ];

    private static ComponentParameterBinding[] InputVectorParameters(
        LogicValue[] values) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)values.Length))),
        new("initialValue", new LogicVectorParameterValue(values)),
    ];

    private static ComponentParameterBinding[] GateParameters() =>
    [
        new("width", new Unsigned32ParameterValue(1)),
        new("fanIn", new Unsigned32ParameterValue(2)),
    ];

    private static ComponentParameterBinding[] ClockParameters() =>
    [
        new("initialValue", new LogicVectorParameterValue([LogicValue.Zero])),
        new("firstTransition", new Unsigned64ParameterValue(1)),
        new("highDuration", new Unsigned64ParameterValue(1)),
        new("lowDuration", new Unsigned64ParameterValue(1)),
    ];

    private static ComponentParameterBinding[] DffParameters() =>
    [
        new("width", new Unsigned32ParameterValue(1)),
        new("edge", new ChoiceParameterValue("rising")),
        new("initialState", new LogicVectorParameterValue([LogicValue.Zero])),
    ];

    private static ComponentParameterBinding[] SinkParameters(uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("radix", new ChoiceParameterValue("binary")),
    ];
}

internal sealed record AuthoredBenchmarkCircuit(
    ProjectRevision Revision,
    Net[] ProbeNets,
    ComponentInstance? StimulusSource);
