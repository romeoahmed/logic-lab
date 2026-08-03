using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private const ulong MaximumGalleryPortCount = 100;

    private async Task AuthorSteeringGallery()
    {
        if (Projection is null)
        {
            return;
        }

        var definitionId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        var placementIndex = 0;
        foreach (var component in SteeringGalleryComponents())
        {
            var target = await PlaceGalleryComponent(
                definitionId,
                component.ContractId,
                component.Parameters,
                component.DisplayName,
                placementIndex++);
            if (target is null)
            {
                return;
            }

            var schema = CoreLibrarySchema.FindContract(
                new ComponentContractKey(CoreLibrarySchema.LibraryId, component.ContractId))
                ?? throw new InvalidOperationException(
                    "A steering gallery component contract is missing from the Core Library.");
            var resolution = schema.ResolvePorts(component.Parameters);
            if (!resolution.TryMaterialize(MaximumGalleryPortCount, out var ports))
            {
                throw new InvalidOperationException(
                    "The bounded steering gallery Port set could not be materialized.");
            }
            foreach (var input in ports.Where(port => port.Direction == PortDirection.Input))
            {
                var source = await PlaceGalleryComponent(
                    definitionId,
                    "source.constant",
                    [
                        new ComponentParameterBinding(
                            "width",
                            new Unsigned32ParameterValue(input.Width)),
                        new ComponentParameterBinding(
                            "value",
                            new LogicVectorParameterValue(Enumerable.Repeat(
                                GalleryInputValue(input.Id),
                                checked((int)input.Width)).ToArray())),
                    ],
                    $"{component.DisplayName} {input.Id}",
                    placementIndex++);
                if (source is null
                    || !await Apply(new ConnectTerminalsIntent([
                        Terminal(definitionId, source.Id, "Q"),
                        Terminal(definitionId, target.Id, input.Id),
                    ])))
                {
                    return;
                }
            }

            foreach (var output in ports.Where(port => port.Direction == PortDirection.Output))
            {
                var sink = await PlaceGalleryComponent(
                    definitionId,
                    "sink.output",
                    [
                        new ComponentParameterBinding(
                            "width",
                            new Unsigned32ParameterValue(output.Width)),
                        new ComponentParameterBinding(
                            "radix",
                            new ChoiceParameterValue("binary")),
                    ],
                    $"{component.DisplayName} {output.Id}",
                    placementIndex++);
                if (sink is null
                    || !await Apply(new ConnectTerminalsIntent([
                        Terminal(definitionId, target.Id, output.Id),
                        Terminal(definitionId, sink.Id, "D"),
                    ])))
                {
                    return;
                }
            }
        }

        Status = "Steering gallery authored with generated Ports. Compile the current Project Revision.";
    }

    private async Task<ComponentInstance?> PlaceGalleryComponent(
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        string displayName,
        int placementIndex)
    {
        var existingIds = Projection!.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract(contractId),
                parameters,
                new ComponentPlacement(new GridPoint(
                    checked((placementIndex % 12) * 4),
                    checked((placementIndex / 12) * 4))),
                displayName)))
        {
            return null;
        }

        return Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => !existingIds.Contains(instance.Id));
    }

    private static LogicValue GalleryInputValue(string portId)
    {
        return portId.EndsWith('0') || portId is "S" or "A"
            ? LogicValue.Zero
            : LogicValue.One;
    }

    private static SteeringGalleryComponent[] SteeringGalleryComponents()
    {
        return
        [
            new("logic.buffer", "Buffer", Width(1)),
            new("logic.and", "AND", GateParameters()),
            new("logic.nand", "NAND", GateParameters()),
            new("logic.or", "OR", GateParameters()),
            new("logic.nor", "NOR", GateParameters()),
            new("logic.xor", "XOR", GateParameters()),
            new("logic.xnor", "XNOR", GateParameters()),
            new("logic.tristate", "Tri-State",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "enablePolarity",
                    new ChoiceParameterValue("activeHigh")),
            ]),
            new("logic.mux", "MUX", SteeringParameters()),
            new("logic.demux", "DEMUX", SteeringParameters()),
            new("logic.decoder", "Decoder",
            [
                new ComponentParameterBinding(
                    "selectorWidth",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "enablePolarity",
                    new ChoiceParameterValue("activeHigh")),
            ]),
            new("logic.priority_encoder", "Priority Encoder",
            [
                new ComponentParameterBinding("inputCount", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "priority",
                    new ChoiceParameterValue("highestIndex")),
            ]),
        ];
    }

    private static ComponentParameterBinding[] Width(uint width)
    {
        return [new ComponentParameterBinding("width", new Unsigned32ParameterValue(width))];
    }

    private static ComponentParameterBinding[] GateParameters()
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)),
        ];
    }

    private static ComponentParameterBinding[] SteeringParameters()
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(1)),
        ];
    }

    private sealed record SteeringGalleryComponent(
        string ContractId,
        string DisplayName,
        ComponentParameterBinding[] Parameters);
}
