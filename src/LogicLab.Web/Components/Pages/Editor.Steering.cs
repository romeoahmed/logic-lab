using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private async Task AuthorSteeringExample()
    {
        if (Projection is null)
        {
            return;
        }

        var definitionId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        var data0 = await PlaceExampleComponent(
            definitionId,
            "source.input",
            InputParameters(1, LogicValue.Zero),
            Text["ExampleData0"],
            new GridPoint(0, 0));
        var data1 = await PlaceExampleComponent(
            definitionId,
            "source.input",
            InputParameters(1, LogicValue.One),
            Text["ExampleData1"],
            new GridPoint(0, 7));
        var select = await PlaceExampleComponent(
            definitionId,
            "source.input",
            InputParameters(1, LogicValue.Zero),
            Text["ExampleSelect"],
            new GridPoint(0, 14));
        var mux = await PlaceExampleComponent(
            definitionId,
            "logic.mux",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(1)),
            ],
            Text["ExampleMultiplexer"],
            new GridPoint(20, 5));
        var output = await PlaceExampleComponent(
            definitionId,
            "sink.output",
            OutputParameters(1),
            Text["ExampleSelectedOutput"],
            new GridPoint(42, 8));
        if (data0 is null || data1 is null || select is null || mux is null || output is null)
        {
            return;
        }

        if (!await AddExampleSignalLabels(
                definitionId,
                [
                    new AnnotationValue("D0", new GridPoint(9, 1), AnnotationAlignment.Start),
                    new AnnotationValue("D1", new GridPoint(9, 8), AnnotationAlignment.Start),
                    new AnnotationValue("S", new GridPoint(9, 15), AnnotationAlignment.Start),
                ])
            || !await ConnectExample(
                definitionId,
                data0,
                "Q",
                mux,
                "D0",
                [new GridPoint(7, 2), new GridPoint(14, 2), new GridPoint(14, 8), new GridPoint(21, 8)])
            || !await ConnectExample(
                definitionId,
                data1,
                "Q",
                mux,
                "D1",
                [new GridPoint(7, 9), new GridPoint(15, 9), new GridPoint(15, 10), new GridPoint(21, 10)])
            || !await ConnectExample(
                definitionId,
                select,
                "Q",
                mux,
                "S",
                [new GridPoint(7, 16), new GridPoint(16, 16), new GridPoint(16, 12), new GridPoint(21, 12)])
            || !await ConnectExample(
                definitionId,
                mux,
                "Q",
                output,
                "D",
                [new GridPoint(39, 10), new GridPoint(43, 10)]))
        {
            return;
        }

        Status = Text["SteeringExampleAuthored"];
    }

    private async Task AuthorArithmeticExample()
    {
        if (Projection is null)
        {
            return;
        }

        var definitionId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        var inputA = await PlaceExampleComponent(
            definitionId,
            "source.input",
            InputParameters(3, LogicValue.One, LogicValue.Zero, LogicValue.One),
            Text["ExampleInputA"],
            new GridPoint(0, 0));
        var inputB = await PlaceExampleComponent(
            definitionId,
            "source.input",
            InputParameters(3, LogicValue.One, LogicValue.One, LogicValue.Zero),
            Text["ExampleInputB"],
            new GridPoint(0, 7));
        var carryIn = await PlaceExampleComponent(
            definitionId,
            "source.input",
            InputParameters(1, LogicValue.Zero),
            Text["ExampleCarryIn"],
            new GridPoint(0, 14));
        var adder = await PlaceExampleComponent(
            definitionId,
            "logic.adder",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(3))],
            Text["ExampleAdder"],
            new GridPoint(20, 5));
        var sum = await PlaceExampleComponent(
            definitionId,
            "sink.output",
            OutputParameters(3),
            Text["ExampleSum"],
            new GridPoint(42, 7));
        var carryOut = await PlaceExampleComponent(
            definitionId,
            "sink.output",
            OutputParameters(1),
            Text["ExampleCarryOut"],
            new GridPoint(42, 14));
        if (inputA is null || inputB is null || carryIn is null
            || adder is null || sum is null || carryOut is null)
        {
            return;
        }

        if (!await AddExampleSignalLabels(
                definitionId,
                [
                    new AnnotationValue("A", new GridPoint(9, 1), AnnotationAlignment.Start),
                    new AnnotationValue("B", new GridPoint(9, 8), AnnotationAlignment.Start),
                    new AnnotationValue("CIN", new GridPoint(9, 15), AnnotationAlignment.Start),
                ])
            || !await ConnectExample(
                definitionId,
                inputA,
                "Q",
                adder,
                "A",
                [new GridPoint(7, 2), new GridPoint(14, 2), new GridPoint(14, 8), new GridPoint(21, 8)])
            || !await ConnectExample(
                definitionId,
                inputB,
                "Q",
                adder,
                "B",
                [new GridPoint(7, 9), new GridPoint(15, 9), new GridPoint(15, 10), new GridPoint(21, 10)])
            || !await ConnectExample(
                definitionId,
                carryIn,
                "Q",
                adder,
                "CIN",
                [new GridPoint(7, 16), new GridPoint(16, 16), new GridPoint(16, 12), new GridPoint(21, 12)])
            || !await ConnectExample(
                definitionId,
                adder,
                "SUM",
                sum,
                "D",
                [new GridPoint(36, 9), new GridPoint(43, 9)])
            || !await ConnectExample(
                definitionId,
                adder,
                "COUT",
                carryOut,
                "D",
                [new GridPoint(36, 11), new GridPoint(39, 11), new GridPoint(39, 16), new GridPoint(43, 16)]))
        {
            return;
        }

        Status = Text["ArithmeticExampleAuthored"];
    }

    private async Task<ComponentInstance?> PlaceExampleComponent(
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        string displayName,
        GridPoint origin)
    {
        var existingIds = Projection!.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract(contractId),
                parameters,
                new ComponentPlacement(origin),
                displayName)))
        {
            return null;
        }

        return Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => !existingIds.Contains(instance.Id));
    }

    private Task<bool> ConnectExample(
        CircuitDefinitionId definitionId,
        ComponentInstance source,
        string sourcePortId,
        ComponentInstance destination,
        string destinationPortId,
        GridPoint[] route) =>
        Apply(new ConnectTerminalsIntent(
            [
                Terminal(definitionId, source.Id, sourcePortId),
                Terminal(definitionId, destination.Id, destinationPortId),
            ],
            destinationNetId: null,
            newJunctionPositions: [],
            routeAdditions: [new OrthogonalWireRoute(route)],
            routeReplacements: []));

    private async Task<bool> AddExampleSignalLabels(
        CircuitDefinitionId definitionId,
        AnnotationValue[] labels)
    {
        foreach (var label in labels)
        {
            if (!await Apply(new CreateAnnotationIntent(definitionId, label)))
            {
                return false;
            }
        }

        return true;
    }

    private static ComponentParameterBinding[] InputParameters(
        uint width,
        params LogicValue[] initialValue) =>
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue(initialValue)),
        ];

    private static ComponentParameterBinding[] OutputParameters(uint width) =>
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ];
}
