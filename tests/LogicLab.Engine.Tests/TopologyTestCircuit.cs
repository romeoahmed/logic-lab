using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Tests;

internal sealed record FlatTopologyCircuit(
    ProjectRevision Revision,
    IReadOnlyDictionary<string, ComponentInstance> Components,
    IReadOnlyDictionary<string, Net> Nets);

internal sealed record HierarchicalTopologyCircuit(
    ProjectRevision Revision,
    CircuitDefinition ChildDefinition,
    ComponentInstance Call,
    IReadOnlyDictionary<string, ComponentInstance> ChildComponents,
    IReadOnlyDictionary<string, Net> ParentNets);

internal static class TopologyTestCircuit
{
    private static readonly LogicValue[] DefaultValues =
        [LogicValue.One, LogicValue.Zero, LogicValue.X, LogicValue.One];

    private static readonly BitSlice[] DefaultSlices =
        [new(0, 1), new(1, 3)];

    public static FlatTopologyCircuit CreateFlat(
        LogicValue[]? values = null,
        BitSlice[]? slices = null,
        uint? extensionWidth = null,
        bool useInputSource = false,
        string zeroSinkRadix = "binary")
    {
        values ??= DefaultValues;
        slices ??= DefaultSlices;
        var concatWidth = slices.Aggregate(
            0U,
            (sum, slice) => checked(sum + slice.Length));
        var resolvedExtensionWidth = extensionWidth ?? checked(concatWidth + 2);
        var revision = CompilerTestCircuit.BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var components = new Dictionary<string, ComponentInstance>();
        (revision, components["source"]) = Place(
            revision,
            definitionId,
            useInputSource ? "source.input" : "source.constant",
            useInputSource
                ? InputParameters(checked((uint)values.Length))
                : ConstantParameters(values));
        (revision, components["split"]) = Place(
            revision,
            definitionId,
            "topology.split",
            SplitParameters(checked((uint)values.Length), slices));
        (revision, components["concat"]) = Place(
            revision,
            definitionId,
            "topology.concat",
            ConcatParameters([.. slices.Select(slice => slice.Length)]));
        (revision, components["zero"]) = Place(
            revision,
            definitionId,
            "topology.zero_extend",
            ExtensionParameters(concatWidth, resolvedExtensionWidth));
        (revision, components["sign"]) = Place(
            revision,
            definitionId,
            "topology.sign_extend",
            ExtensionParameters(concatWidth, resolvedExtensionWidth));
        (revision, components["zeroSink"]) = Place(
            revision,
            definitionId,
            "sink.output",
            SinkParameters(resolvedExtensionWidth, zeroSinkRadix));
        (revision, components["signSink"]) = Place(
            revision,
            definitionId,
            "sink.output",
            SinkParameters(resolvedExtensionWidth, "hex"));

        var nets = new Dictionary<string, Net>();
        (revision, nets["source"]) = Connect(revision,
            Port(definitionId, components["source"], "Q"),
            Port(definitionId, components["split"], "D"));
        (revision, nets["firstSlice"]) = Connect(revision,
            Port(definitionId, components["split"], "Q0"),
            Port(definitionId, components["concat"], "D0"));
        (revision, nets["secondSlice"]) = Connect(revision,
            Port(definitionId, components["split"], "Q1"),
            Port(definitionId, components["concat"], "D1"));
        (revision, nets["concat"]) = Connect(revision,
            Port(definitionId, components["concat"], "Q"),
            Port(definitionId, components["zero"], "D"),
            Port(definitionId, components["sign"], "D"));
        (revision, nets["zero"]) = Connect(revision,
            Port(definitionId, components["zero"], "Q"),
            Port(definitionId, components["zeroSink"], "D"));
        (revision, nets["sign"]) = Connect(revision,
            Port(definitionId, components["sign"], "Q"),
            Port(definitionId, components["signSink"], "D"));

        return new FlatTopologyCircuit(revision, components, nets);
    }

    public static ProjectRevision CreateUnconnectedDynamicPortCircuit(
        string contractId,
        int itemCount)
    {
        var revision = CompilerTestCircuit.BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var parameters = DynamicPortParameters(contractId, itemCount);
        (revision, _) = Place(revision, definitionId, contractId, parameters);
        return revision;
    }

    public static ProjectRevision CreateHierarchicalUnconnectedSplit(int sliceCount)
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Dynamic ports", [])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Dynamic ports");
        (revision, _) = Place(
            revision,
            child.Id,
            "topology.split",
            DynamicPortParameters("topology.split", sliceCount));
        (revision, _) = PlaceDefinition(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            child.Id);
        return revision;
    }

    public static HierarchicalTopologyCircuit CreateHierarchical()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Topology child",
                [
                    new DefinitionPortDeclaration(
                        "ZERO",
                        PortDirection.Output,
                        6,
                        new DefinitionPortPlacement(
                            new GridPoint(12, 0),
                            CardinalDirection.East)),
                    new DefinitionPortDeclaration(
                        "SIGN",
                        PortDirection.Output,
                        6,
                        new DefinitionPortPlacement(
                            new GridPoint(12, 4),
                            CardinalDirection.East)),
                ])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Topology child");
        var zeroPort = child.Ports.Single(port => port.DisplayName == "ZERO");
        var signPort = child.Ports.Single(port => port.DisplayName == "SIGN");
        var childComponents = new Dictionary<string, ComponentInstance>();
        (revision, childComponents["source"]) = Place(
            revision,
            child.Id,
            "source.constant",
            ConstantParameters(DefaultValues));
        (revision, childComponents["split"]) = Place(
            revision,
            child.Id,
            "topology.split",
            SplitParameters(4, DefaultSlices));
        (revision, childComponents["concat"]) = Place(
            revision,
            child.Id,
            "topology.concat",
            ConcatParameters(1, 3));
        (revision, childComponents["zero"]) = Place(
            revision,
            child.Id,
            "topology.zero_extend",
            ExtensionParameters(4, 6));
        (revision, childComponents["sign"]) = Place(
            revision,
            child.Id,
            "topology.sign_extend",
            ExtensionParameters(4, 6));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["source"], "Q"),
            Port(child.Id, childComponents["split"], "D"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["split"], "Q0"),
            Port(child.Id, childComponents["concat"], "D0"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["split"], "Q1"),
            Port(child.Id, childComponents["concat"], "D1"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["concat"], "Q"),
            Port(child.Id, childComponents["zero"], "D"),
            Port(child.Id, childComponents["sign"], "D"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["zero"], "Q"),
            new DefinitionTerminalReference(child.Id, zeroPort.Id));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["sign"], "Q"),
            new DefinitionTerminalReference(child.Id, signPort.Id));

        var entryId = revision.Document.EntryCircuitDefinitionId;
        (revision, var call) = PlaceDefinition(revision, entryId, child.Id);
        (revision, var zeroSink) = Place(
            revision,
            entryId,
            "sink.output",
            SinkParameters(6, "binary"));
        (revision, var signSink) = Place(
            revision,
            entryId,
            "sink.output",
            SinkParameters(6, "hex"));
        var parentNets = new Dictionary<string, Net>();
        (revision, parentNets["zero"]) = Connect(revision,
            Port(entryId, call, zeroPort.Id.Value),
            Port(entryId, zeroSink, "D"));
        (revision, parentNets["sign"]) = Connect(revision,
            Port(entryId, call, signPort.Id.Value),
            Port(entryId, signSink, "D"));
        var resolvedChild = revision.Document.FindCircuitDefinition(child.Id)!;

        return new HierarchicalTopologyCircuit(
            revision,
            resolvedChild,
            call,
            childComponents,
            parentNets);
    }

    private static (
        ProjectRevision Revision,
        ComponentInstance Instance) Place(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var before = revision.Document.FindCircuitDefinition(definitionId)!
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        var committed = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(new GridPoint(before.Count * 4, 0)))));
        var instance = committed.Document.FindCircuitDefinition(definitionId)!
            .ComponentInstances.Single(item => !before.Contains(item.Id));
        return (committed, instance);
    }

    private static (
        ProjectRevision Revision,
        ComponentInstance Instance) PlaceDefinition(
        ProjectRevision revision,
        CircuitDefinitionId containingDefinitionId,
        CircuitDefinitionId targetDefinitionId)
    {
        var before = revision.Document.FindCircuitDefinition(containingDefinitionId)!
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        var committed = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                containingDefinitionId,
                new CircuitDefinitionComponentTarget(targetDefinitionId),
                [],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Topology child occurrence")));
        var instance = committed.Document.FindCircuitDefinition(containingDefinitionId)!
            .ComponentInstances.Single(item => !before.Contains(item.Id));
        return (committed, instance);
    }

    private static (ProjectRevision Revision, Net Net) Connect(
        ProjectRevision revision,
        params AuthoredTerminalReference[] terminals)
    {
        var committed = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(terminals)));
        var definition = committed.Document.FindCircuitDefinition(
            terminals[0].CircuitDefinitionId)!;
        var net = definition.Nets.Single(candidate =>
            terminals.All(candidate.Terminals.Contains));
        return (committed, net);
    }

    private static InstanceTerminalReference Port(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }

    private static ComponentParameterBinding[] ConstantParameters(
        LogicValue[] values)
    {
        return
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(checked((uint)values.Length))),
            new ComponentParameterBinding(
                "value",
                new LogicVectorParameterValue(values)),
        ];
    }

    private static ComponentParameterBinding[] InputParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue(
                    [.. Enumerable.Repeat(
                        LogicValue.Zero,
                        checked((int)width))])),
        ];
    }

    private static ComponentParameterBinding[] SplitParameters(
        uint width,
        IReadOnlyList<BitSlice> slices)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("slices", new SlicesParameterValue(slices)),
        ];
    }

    private static ComponentParameterBinding[] ConcatParameters(params uint[] widths)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidths",
                new WidthsParameterValue(widths)),
        ];
    }

    private static ComponentParameterBinding[] ExtensionParameters(
        uint inputWidth,
        uint outputWidth)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidth",
                new Unsigned32ParameterValue(inputWidth)),
            new ComponentParameterBinding(
                "outputWidth",
                new Unsigned32ParameterValue(outputWidth)),
        ];
    }

    private static ComponentParameterBinding[] DynamicPortParameters(
        string contractId,
        int itemCount)
    {
        return contractId switch
        {
            "topology.split" => SplitParameters(
                checked((uint)itemCount),
                [.. Enumerable.Range(0, itemCount).Select(index => new BitSlice(checked((uint)index), 1))]),
            "topology.concat" => ConcatParameters(
                [.. Enumerable.Repeat(1U, itemCount)]),
            _ => throw new ArgumentOutOfRangeException(
                nameof(contractId),
                contractId,
                "The dynamic Port contract is unsupported."),
        };
    }

    private static ComponentParameterBinding[] SinkParameters(
        uint width,
        string radix)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue(radix)),
        ];
    }
}
