using System.Text.Json.Serialization;

namespace LogicLab.Web.Scene;

// Closed-union configuration follows the System.Text.Json polymorphism contract:
// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SelectSourcesSceneIntentV1), "selectSources")]
[JsonDerivedType(typeof(PlaceComponentSceneIntentV1), "placeComponent")]
[JsonDerivedType(typeof(MoveComponentsSceneIntentV1), "moveComponents")]
[JsonDerivedType(typeof(MoveDefinitionPortsSceneIntentV1), "moveDefinitionPorts")]
[JsonDerivedType(typeof(MoveAnnotationsSceneIntentV1), "moveAnnotations")]
[JsonDerivedType(typeof(CommitWireSceneIntentV1), "commitWire")]
[JsonDerivedType(typeof(AddJunctionSceneIntentV1), "addJunction")]
[JsonDerivedType(typeof(RemoveJunctionSceneIntentV1), "removeJunction")]
[JsonDerivedType(typeof(SetWireRouteSceneIntentV1), "setWireRoute")]
[JsonDerivedType(typeof(ToggleProbeSceneIntentV1), "toggleProbe")]
public abstract record SceneIntentV1
{
    private protected SceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId)
    {
        BuildFingerprint = buildFingerprint;
        SceneVersion = sceneVersion;
        ProjectionVersion = projectionVersion;
        CircuitDefinitionId = circuitDefinitionId;
    }

    public string BuildFingerprint { get; }

    public ulong SceneVersion { get; }

    public ulong ProjectionVersion { get; }

    public string CircuitDefinitionId { get; }
}

public sealed record SelectSourcesSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public SelectSourcesSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        IReadOnlyList<SceneSourceRefV1> sources,
        string selectionMode)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Sources = SceneIntentCollections.Copy(sources);
        SelectionMode = selectionMode;
    }

    public IReadOnlyList<SceneSourceRefV1> Sources { get; }

    public string SelectionMode { get; }
}

public sealed record PlaceComponentSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public PlaceComponentSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        SceneComponentTargetV1 target,
        IReadOnlyList<SceneParameterBindingV1> parameters,
        SceneComponentPlacementV1 placement,
        string? displayName,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(placement);
        Target = target;
        Parameters = SceneIntentCollections.Copy(parameters);
        Placement = placement;
        DisplayName = displayName;
        SnapModifier = snapModifier;
    }

    public SceneComponentTargetV1 Target { get; }

    public IReadOnlyList<SceneParameterBindingV1> Parameters { get; }

    public SceneComponentPlacementV1 Placement { get; }

    public string? DisplayName { get; }

    public string SnapModifier { get; }
}

public sealed record MoveComponentsSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public MoveComponentsSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        IReadOnlyList<SceneComponentMoveV1> moves,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        Moves = SceneIntentCollections.Copy(moves);
        SnapModifier = snapModifier;
    }

    public IReadOnlyList<SceneComponentMoveV1> Moves { get; }

    public string SnapModifier { get; }
}

public sealed record MoveDefinitionPortsSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public MoveDefinitionPortsSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        IReadOnlyList<SceneDefinitionPortMoveV1> moves,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        Moves = SceneIntentCollections.Copy(moves);
        SnapModifier = snapModifier;
    }

    public IReadOnlyList<SceneDefinitionPortMoveV1> Moves { get; }

    public string SnapModifier { get; }
}

public sealed record MoveAnnotationsSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public MoveAnnotationsSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        IReadOnlyList<SceneAnnotationMoveV1> moves,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        Moves = SceneIntentCollections.Copy(moves);
        SnapModifier = snapModifier;
    }

    public IReadOnlyList<SceneAnnotationMoveV1> Moves { get; }

    public string SnapModifier { get; }
}

public sealed record CommitWireSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public CommitWireSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        IReadOnlyList<SceneTerminalRefV1> terminals,
        SceneSourceRefV1? destinationNet,
        IReadOnlyList<SceneGridPointV1> newJunctionPositions,
        IReadOnlyList<SceneWireRouteV1> routeAdditions,
        IReadOnlyList<SceneWireReplacementV1> routeReplacements,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        Terminals = SceneIntentCollections.Copy(terminals);
        DestinationNet = destinationNet;
        NewJunctionPositions = SceneIntentCollections.Copy(newJunctionPositions);
        RouteAdditions = SceneIntentCollections.Copy(routeAdditions);
        RouteReplacements = SceneIntentCollections.Copy(routeReplacements);
        SnapModifier = snapModifier;
    }

    public IReadOnlyList<SceneTerminalRefV1> Terminals { get; }

    public SceneSourceRefV1? DestinationNet { get; }

    public IReadOnlyList<SceneGridPointV1> NewJunctionPositions { get; }

    public IReadOnlyList<SceneWireRouteV1> RouteAdditions { get; }

    public IReadOnlyList<SceneWireReplacementV1> RouteReplacements { get; }

    public string SnapModifier { get; }
}

public sealed record AddJunctionSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public AddJunctionSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        SceneSourceRefV1 net,
        SceneGridPointV1 position,
        IReadOnlyList<SceneWireRouteV1> routeAdditions,
        IReadOnlyList<SceneWireReplacementV1> routeReplacements,
        IReadOnlyList<SceneSourceRefV1> routeRemovals,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(net);
        ArgumentNullException.ThrowIfNull(position);
        Net = net;
        Position = position;
        RouteAdditions = SceneIntentCollections.Copy(routeAdditions);
        RouteReplacements = SceneIntentCollections.Copy(routeReplacements);
        RouteRemovals = SceneIntentCollections.Copy(routeRemovals);
        SnapModifier = snapModifier;
    }

    public SceneSourceRefV1 Net { get; }

    public SceneGridPointV1 Position { get; }

    public IReadOnlyList<SceneWireRouteV1> RouteAdditions { get; }

    public IReadOnlyList<SceneWireReplacementV1> RouteReplacements { get; }

    public IReadOnlyList<SceneSourceRefV1> RouteRemovals { get; }

    public string SnapModifier { get; }
}

public sealed record RemoveJunctionSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public RemoveJunctionSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        SceneSourceRefV1 junction,
        IReadOnlyList<SceneJunctionRemovalPartitionV1> resultingPartitions,
        IReadOnlyList<SceneWireReplacementV1> routeReplacements,
        IReadOnlyList<SceneSourceRefV1> routeRemovals,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(junction);
        Junction = junction;
        ResultingPartitions = SceneIntentCollections.Copy(resultingPartitions);
        RouteReplacements = SceneIntentCollections.Copy(routeReplacements);
        RouteRemovals = SceneIntentCollections.Copy(routeRemovals);
        SnapModifier = snapModifier;
    }

    public SceneSourceRefV1 Junction { get; }

    public IReadOnlyList<SceneJunctionRemovalPartitionV1> ResultingPartitions { get; }

    public IReadOnlyList<SceneWireReplacementV1> RouteReplacements { get; }

    public IReadOnlyList<SceneSourceRefV1> RouteRemovals { get; }

    public string SnapModifier { get; }
}

public sealed record SetWireRouteSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public SetWireRouteSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        SceneSourceRefV1 wireGeometry,
        SceneWireRouteV1 route,
        string snapModifier)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(wireGeometry);
        ArgumentNullException.ThrowIfNull(route);
        WireGeometry = wireGeometry;
        Route = route;
        SnapModifier = snapModifier;
    }

    public SceneSourceRefV1 WireGeometry { get; }

    public SceneWireRouteV1 Route { get; }

    public string SnapModifier { get; }
}

public sealed record ToggleProbeSceneIntentV1 : SceneIntentV1
{
    [JsonConstructor]
    public ToggleProbeSceneIntentV1(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        string circuitDefinitionId,
        SceneElaboratedNetRefV1 net)
        : base(buildFingerprint, sceneVersion, projectionVersion, circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(net);
        Net = net;
    }

    public SceneElaboratedNetRefV1 Net { get; }
}

public sealed record SceneSelectionV1
{
    public SceneSelectionV1(
        IReadOnlyList<SceneSourceRefV1> sources,
        string selectionMode)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Sources = SceneIntentCollections.Copy(sources);
        SelectionMode = selectionMode;
    }

    public IReadOnlyList<SceneSourceRefV1> Sources { get; }

    public string SelectionMode { get; }
}

public sealed record SceneGridPointV1(int X, int Y);

public sealed record SceneComponentPlacementV1(
    SceneGridPointV1 Origin,
    int QuarterTurnsClockwise,
    bool Reflected);

public sealed record SceneComponentMoveV1(
    SceneSourceRefV1 Component,
    SceneComponentPlacementV1 Placement);

public sealed record SceneDefinitionPortPlacementV1(
    SceneGridPointV1 Position,
    string Facing);

public sealed record SceneDefinitionPortMoveV1(
    SceneSourceRefV1 Port,
    SceneDefinitionPortPlacementV1 Placement);

public sealed record SceneAnnotationMoveV1(
    SceneSourceRefV1 Annotation,
    SceneGridPointV1 Position);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SceneLibraryComponentTargetV1), "libraryContract")]
[JsonDerivedType(typeof(SceneCircuitDefinitionTargetV1), "circuitDefinition")]
public abstract record SceneComponentTargetV1;

public sealed record SceneLibraryComponentTargetV1(string LibraryId, string ContractId)
    : SceneComponentTargetV1;

public sealed record SceneCircuitDefinitionTargetV1(string CircuitDefinitionId)
    : SceneComponentTargetV1;

public sealed record SceneParameterBindingV1(
    string ParameterId,
    SceneParameterValueV1 Value);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SceneUnsigned32ParameterV1), "unsigned32")]
[JsonDerivedType(typeof(SceneUnsigned64ParameterV1), "unsigned64")]
[JsonDerivedType(typeof(SceneChoiceParameterV1), "enum")]
[JsonDerivedType(typeof(SceneLogicVectorParameterV1), "logicVector")]
[JsonDerivedType(typeof(SceneWidthsParameterV1), "unsigned32List")]
[JsonDerivedType(typeof(SceneSlicesParameterV1), "sliceList")]
[JsonDerivedType(typeof(SceneMemoryImageParameterV1), "memoryImage")]
public abstract record SceneParameterValueV1;

public sealed record SceneUnsigned32ParameterV1(uint Value) : SceneParameterValueV1;

public sealed record SceneUnsigned64ParameterV1(
    [property: JsonPropertyName("decimal")] string DecimalText) : SceneParameterValueV1;

public sealed record SceneChoiceParameterV1(string Value) : SceneParameterValueV1;

public sealed record SceneLogicVectorParameterV1(string Bits) : SceneParameterValueV1;

public sealed record SceneWidthsParameterV1 : SceneParameterValueV1
{
    [JsonConstructor]
    public SceneWidthsParameterV1(IReadOnlyList<uint> values)
    {
        Values = SceneIntentCollections.Copy(values);
    }

    public IReadOnlyList<uint> Values { get; }
}

public sealed record SceneBitSliceV1(uint Offset, uint Length);

public sealed record SceneSlicesParameterV1 : SceneParameterValueV1
{
    [JsonConstructor]
    public SceneSlicesParameterV1(IReadOnlyList<SceneBitSliceV1> values)
    {
        Values = SceneIntentCollections.Copy(values);
    }

    public IReadOnlyList<SceneBitSliceV1> Values { get; }
}

public sealed record SceneMemoryImageParameterV1(string MemoryImageId)
    : SceneParameterValueV1;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SceneDefinitionTerminalRefV1), "definitionTerminal")]
[JsonDerivedType(typeof(SceneInstanceTerminalRefV1), "instanceTerminal")]
public abstract record SceneTerminalRefV1
{
    private protected SceneTerminalRefV1(string circuitDefinitionId)
    {
        CircuitDefinitionId = circuitDefinitionId;
    }

    public string CircuitDefinitionId { get; }
}

public sealed record SceneDefinitionTerminalRefV1 : SceneTerminalRefV1
{
    [JsonConstructor]
    public SceneDefinitionTerminalRefV1(string circuitDefinitionId, string portId)
        : base(circuitDefinitionId)
    {
        PortId = portId;
    }

    public string PortId { get; }
}

public sealed record SceneInstanceTerminalRefV1 : SceneTerminalRefV1
{
    [JsonConstructor]
    public SceneInstanceTerminalRefV1(
        string circuitDefinitionId,
        string componentInstanceId,
        string portId)
        : base(circuitDefinitionId)
    {
        ComponentInstanceId = componentInstanceId;
        PortId = portId;
    }

    public string ComponentInstanceId { get; }

    public string PortId { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SceneUnroutedWireRouteV1), "unrouted")]
[JsonDerivedType(typeof(SceneOrthogonalWireRouteV1), "orthogonal")]
public abstract record SceneWireRouteV1;

public sealed record SceneUnroutedWireRouteV1 : SceneWireRouteV1;

public sealed record SceneOrthogonalWireRouteV1 : SceneWireRouteV1
{
    [JsonConstructor]
    public SceneOrthogonalWireRouteV1(IReadOnlyList<SceneGridPointV1> points)
    {
        Points = SceneIntentCollections.Copy(points);
    }

    public IReadOnlyList<SceneGridPointV1> Points { get; }
}

public sealed record SceneWireReplacementV1(
    SceneSourceRefV1 WireGeometry,
    SceneWireRouteV1 Route);

public sealed record SceneNetPartitionV1
{
    [JsonConstructor]
    public SceneNetPartitionV1(
        IReadOnlyList<SceneTerminalRefV1> terminals,
        IReadOnlyList<SceneSourceRefV1> junctions,
        IReadOnlyList<SceneSourceRefV1> wireGeometries)
    {
        Terminals = SceneIntentCollections.Copy(terminals);
        Junctions = SceneIntentCollections.Copy(junctions);
        WireGeometries = SceneIntentCollections.Copy(wireGeometries);
    }

    public IReadOnlyList<SceneTerminalRefV1> Terminals { get; }

    public IReadOnlyList<SceneSourceRefV1> Junctions { get; }

    public IReadOnlyList<SceneSourceRefV1> WireGeometries { get; }
}

public sealed record SceneJunctionRemovalPartitionV1
{
    [JsonConstructor]
    public SceneJunctionRemovalPartitionV1(
        SceneNetPartitionV1 membership,
        IReadOnlyList<SceneWireRouteV1> routeAdditions)
    {
        ArgumentNullException.ThrowIfNull(membership);
        Membership = membership;
        RouteAdditions = SceneIntentCollections.Copy(routeAdditions);
    }

    public SceneNetPartitionV1 Membership { get; }

    public IReadOnlyList<SceneWireRouteV1> RouteAdditions { get; }
}

public sealed record SceneElaboratedNetRefV1
{
    [JsonConstructor]
    public SceneElaboratedNetRefV1(
        SceneSourceRefV1 authoredNet,
        SceneHierarchyPathV1 hierarchyPath)
    {
        ArgumentNullException.ThrowIfNull(authoredNet);
        ArgumentNullException.ThrowIfNull(hierarchyPath);
        AuthoredNet = authoredNet;
        HierarchyPath = hierarchyPath;
    }

    public SceneSourceRefV1 AuthoredNet { get; }

    public SceneHierarchyPathV1 HierarchyPath { get; }
}

public sealed record SceneHierarchyPathV1
{
    [JsonConstructor]
    public SceneHierarchyPathV1(
        string entryCircuitDefinitionId,
        IReadOnlyList<SceneHierarchyStepV1> steps)
    {
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        Steps = SceneIntentCollections.Copy(steps);
    }

    public string EntryCircuitDefinitionId { get; }

    public IReadOnlyList<SceneHierarchyStepV1> Steps { get; }
}

public sealed record SceneHierarchyStepV1(
    string ContainingCircuitDefinitionId,
    string ComponentInstanceId);

file static class SceneIntentCollections
{
    public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (copy.Any(static item => item is null))
        {
            throw new ArgumentException(
                "The collection must not contain null elements.",
                nameof(items));
        }

        return Array.AsReadOnly(copy);
    }
}
