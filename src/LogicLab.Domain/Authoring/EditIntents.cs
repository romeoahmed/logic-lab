using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public abstract record EditIntent
{
    private protected EditIntent()
    {
    }
}

public sealed record CreateCircuitDefinitionIntent : EditIntent
{
    public CreateCircuitDefinitionIntent(
        string displayName,
        IReadOnlyList<DefinitionPortDeclaration> ports)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        DisplayName = displayName;
        Ports = AuthoringInput.CopyRequiredReferences(ports, nameof(ports));
    }

    public string DisplayName { get; }

    public ReadOnlyCollection<DefinitionPortDeclaration> Ports { get; }
}

public sealed record SetEntryCircuitDefinitionIntent : EditIntent
{
    public SetEntryCircuitDefinitionIntent(CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }
}

public sealed record PlaceComponentInstanceIntent : EditIntent
{
    public PlaceComponentInstanceIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentContractKey contractKey,
        IReadOnlyList<ComponentParameterBinding> parameters,
        ComponentPlacement placement,
        string? displayName = null)
        : this(
            circuitDefinitionId,
            new LibraryComponentTarget(contractKey),
            parameters,
            placement,
            displayName)
    {
    }

    public PlaceComponentInstanceIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        ComponentPlacement placement,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(target);
        CircuitDefinitionId = circuitDefinitionId;
        Target = target;
        Parameters = AuthoringInput.CopyRequiredReferences(parameters, nameof(parameters));
        Placement = placement;
        DisplayName = displayName;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentTarget Target { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ComponentPlacement Placement { get; }

    public string? DisplayName { get; }
}

public sealed record ConnectTerminalsIntent : EditIntent
{
    public ConnectTerminalsIntent(IReadOnlyList<AuthoredTerminalReference> terminals)
        : this(terminals, null, [], [], [])
    {
    }

    public ConnectTerminalsIntent(
        IReadOnlyList<AuthoredTerminalReference> terminals,
        NetId? destinationNetId,
        IReadOnlyList<GridPoint> newJunctionPositions,
        IReadOnlyList<WireRoute> routeAdditions,
        IReadOnlyList<WireGeometryReplacement> routeReplacements)
    {
        ArgumentNullException.ThrowIfNull(newJunctionPositions);
        Terminals = AuthoringInput.CopyRequiredReferences(terminals, nameof(terminals));
        DestinationNetId = destinationNetId;
        NewJunctionPositions = Array.AsReadOnly(newJunctionPositions.ToArray());
        RouteAdditions = AuthoringInput.CopyRequiredReferences(
            routeAdditions,
            nameof(routeAdditions));
        RouteReplacements = AuthoringInput.CopyRequiredReferences(
            routeReplacements,
            nameof(routeReplacements));
    }

    public ReadOnlyCollection<AuthoredTerminalReference> Terminals { get; }

    public NetId? DestinationNetId { get; }

    public ReadOnlyCollection<GridPoint> NewJunctionPositions { get; }

    public ReadOnlyCollection<WireRoute> RouteAdditions { get; }

    public ReadOnlyCollection<WireGeometryReplacement> RouteReplacements { get; }
}

public sealed record WireGeometryReplacement
{
    public WireGeometryReplacement(WireGeometryId wireGeometryId, WireRoute route)
    {
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        ArgumentNullException.ThrowIfNull(route);
        WireGeometryId = wireGeometryId;
        Route = route;
    }

    public WireGeometryId WireGeometryId { get; }

    public WireRoute Route { get; }
}

public sealed record NetPartition
{
    public NetPartition(
        IReadOnlyList<AuthoredTerminalReference> terminals,
        IReadOnlyList<JunctionId> junctionIds,
        IReadOnlyList<WireGeometryId> wireGeometryIds)
    {
        Terminals = AuthoringInput.CopyRequiredReferences(terminals, nameof(terminals));
        JunctionIds = AuthoringInput.CopyRequiredReferences(junctionIds, nameof(junctionIds));
        WireGeometryIds = AuthoringInput.CopyRequiredReferences(
            wireGeometryIds,
            nameof(wireGeometryIds));
    }

    public ReadOnlyCollection<AuthoredTerminalReference> Terminals { get; }

    public ReadOnlyCollection<JunctionId> JunctionIds { get; }

    public ReadOnlyCollection<WireGeometryId> WireGeometryIds { get; }
}

public sealed record JunctionRemovalPartition
{
    public JunctionRemovalPartition(
        NetPartition membership,
        IReadOnlyList<WireRoute> routeAdditions)
    {
        ArgumentNullException.ThrowIfNull(membership);
        Membership = membership;
        RouteAdditions = AuthoringInput.CopyRequiredReferences(
            routeAdditions,
            nameof(routeAdditions));
    }

    public NetPartition Membership { get; }

    public ReadOnlyCollection<WireRoute> RouteAdditions { get; }
}

public sealed record MergeNetsIntent : EditIntent
{
    public MergeNetsIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId destinationNetId,
        IReadOnlyList<NetId> sourceNetIds)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(destinationNetId);
        CircuitDefinitionId = circuitDefinitionId;
        DestinationNetId = destinationNetId;
        SourceNetIds = AuthoringInput.CopyRequiredReferences(
            sourceNetIds,
            nameof(sourceNetIds));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId DestinationNetId { get; }

    public ReadOnlyCollection<NetId> SourceNetIds { get; }
}

public sealed record SplitNetIntent : EditIntent
{
    public SplitNetIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId netId,
        IReadOnlyList<NetPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(netId);
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Partitions = AuthoringInput.CopyRequiredReferences(partitions, nameof(partitions));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId NetId { get; }

    public ReadOnlyCollection<NetPartition> Partitions { get; }
}

public sealed record AddJunctionIntent : EditIntent
{
    public AddJunctionIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId netId,
        GridPoint position,
        IReadOnlyList<WireRoute> routeAdditions,
        IReadOnlyList<WireGeometryReplacement> routeReplacements,
        IReadOnlyList<WireGeometryId> routeRemovals)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(netId);
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Position = position;
        RouteAdditions = AuthoringInput.CopyRequiredReferences(
            routeAdditions,
            nameof(routeAdditions));
        RouteReplacements = AuthoringInput.CopyRequiredReferences(
            routeReplacements,
            nameof(routeReplacements));
        RouteRemovals = AuthoringInput.CopyRequiredReferences(
            routeRemovals,
            nameof(routeRemovals));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId NetId { get; }

    public GridPoint Position { get; }

    public ReadOnlyCollection<WireRoute> RouteAdditions { get; }

    public ReadOnlyCollection<WireGeometryReplacement> RouteReplacements { get; }

    public ReadOnlyCollection<WireGeometryId> RouteRemovals { get; }
}

public sealed record RemoveJunctionIntent : EditIntent
{
    public RemoveJunctionIntent(
        CircuitDefinitionId circuitDefinitionId,
        JunctionId junctionId,
        IReadOnlyList<JunctionRemovalPartition> resultingPartitions,
        IReadOnlyList<WireGeometryReplacement> routeReplacements,
        IReadOnlyList<WireGeometryId> routeRemovals)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(junctionId);
        CircuitDefinitionId = circuitDefinitionId;
        JunctionId = junctionId;
        ResultingPartitions = AuthoringInput.CopyRequiredReferences(
            resultingPartitions,
            nameof(resultingPartitions));
        RouteReplacements = AuthoringInput.CopyRequiredReferences(
            routeReplacements,
            nameof(routeReplacements));
        RouteRemovals = AuthoringInput.CopyRequiredReferences(
            routeRemovals,
            nameof(routeRemovals));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public JunctionId JunctionId { get; }

    public ReadOnlyCollection<JunctionRemovalPartition> ResultingPartitions { get; }

    public ReadOnlyCollection<WireGeometryReplacement> RouteReplacements { get; }

    public ReadOnlyCollection<WireGeometryId> RouteRemovals { get; }
}

public sealed record AddWireGeometryIntent : EditIntent
{
    public AddWireGeometryIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId netId,
        WireRoute route)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(netId);
        ArgumentNullException.ThrowIfNull(route);
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Route = route;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId NetId { get; }

    public WireRoute Route { get; }
}

public sealed record SetWireGeometryIntent : EditIntent
{
    public SetWireGeometryIntent(
        CircuitDefinitionId circuitDefinitionId,
        WireGeometryId wireGeometryId,
        WireRoute route)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        ArgumentNullException.ThrowIfNull(route);
        CircuitDefinitionId = circuitDefinitionId;
        WireGeometryId = wireGeometryId;
        Route = route;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public WireGeometryId WireGeometryId { get; }

    public WireRoute Route { get; }
}

public sealed record RemoveWireGeometryIntent : EditIntent
{
    public RemoveWireGeometryIntent(
        CircuitDefinitionId circuitDefinitionId,
        WireGeometryId wireGeometryId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        CircuitDefinitionId = circuitDefinitionId;
        WireGeometryId = wireGeometryId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public WireGeometryId WireGeometryId { get; }
}

public sealed record ComponentMove
{
    public ComponentMove(
        ComponentInstanceId componentInstanceId,
        ComponentPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ComponentInstanceId = componentInstanceId;
        Placement = placement;
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public ComponentPlacement Placement { get; }
}

public sealed record MoveComponentInstancesIntent : EditIntent
{
    public MoveComponentInstancesIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<ComponentMove> moves)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
        Moves = AuthoringInput.CopyRequiredReferences(moves, nameof(moves));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<ComponentMove> Moves { get; }
}

public sealed record RenameCircuitDefinitionIntent : EditIntent
{
    public RenameCircuitDefinitionIntent(
        CircuitDefinitionId circuitDefinitionId,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(displayName);
        CircuitDefinitionId = circuitDefinitionId;
        DisplayName = displayName;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public string DisplayName { get; }
}

public abstract record DefinitionPortContract
{
    private protected DefinitionPortContract(DefinitionPortDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        Declaration = declaration;
    }

    public DefinitionPortDeclaration Declaration { get; }
}

public sealed record RetainedDefinitionPortContract : DefinitionPortContract
{
    public RetainedDefinitionPortContract(
        DefinitionPortId definitionPortId,
        DefinitionPortDeclaration declaration)
        : base(declaration)
    {
        ArgumentNullException.ThrowIfNull(definitionPortId);
        DefinitionPortId = definitionPortId;
    }

    public DefinitionPortId DefinitionPortId { get; }
}

public sealed record NewDefinitionPortContract : DefinitionPortContract
{
    public NewDefinitionPortContract(DefinitionPortDeclaration declaration)
        : base(declaration)
    {
    }
}

public sealed record PortTerminalMigration
{
    public PortTerminalMigration(DefinitionPortId oldPortId, int? newPortIndex)
    {
        ArgumentNullException.ThrowIfNull(oldPortId);
        OldPortId = oldPortId;
        NewPortIndex = newPortIndex;
    }

    public DefinitionPortId OldPortId { get; }

    public int? NewPortIndex { get; }
}

public sealed record CallSiteTerminalMigration
{
    public CallSiteTerminalMigration(
        CircuitDefinitionId containingCircuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        IReadOnlyList<PortTerminalMigration> ports)
    {
        ArgumentNullException.ThrowIfNull(containingCircuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ContainingCircuitDefinitionId = containingCircuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        Ports = AuthoringInput.CopyRequiredReferences(ports, nameof(ports));
    }

    public CircuitDefinitionId ContainingCircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public ReadOnlyCollection<PortTerminalMigration> Ports { get; }
}

public sealed record ChangePublicPortContractIntent : EditIntent
{
    public ChangePublicPortContractIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<DefinitionPortContract> ports,
        IReadOnlyList<CallSiteTerminalMigration> callSites)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
        Ports = AuthoringInput.CopyRequiredReferences(ports, nameof(ports));
        CallSites = AuthoringInput.CopyRequiredReferences(callSites, nameof(callSites));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<DefinitionPortContract> Ports { get; }

    public ReadOnlyCollection<CallSiteTerminalMigration> CallSites { get; }
}

public sealed record DefinitionPortMove
{
    public DefinitionPortMove(
        DefinitionPortId definitionPortId,
        DefinitionPortPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(definitionPortId);
        DefinitionPortId = definitionPortId;
        Placement = placement;
    }

    public DefinitionPortId DefinitionPortId { get; }

    public DefinitionPortPlacement Placement { get; }
}

public sealed record MoveDefinitionPortsIntent : EditIntent
{
    public MoveDefinitionPortsIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<DefinitionPortMove> moves)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
        Moves = AuthoringInput.CopyRequiredReferences(moves, nameof(moves));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<DefinitionPortMove> Moves { get; }
}

public sealed record RemoveCircuitDefinitionIntent : EditIntent
{
    public RemoveCircuitDefinitionIntent(CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }
}

public sealed record RenameComponentInstanceIntent : EditIntent
{
    public RenameComponentInstanceIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        string? displayName)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        DisplayName = displayName;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string? DisplayName { get; }
}

public sealed record SetInstanceParametersIntent : EditIntent
{
    public SetInstanceParametersIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        Parameters = AuthoringInput.CopyRequiredReferences(parameters, nameof(parameters));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }
}

public sealed record InstancePortMigration
{
    public InstancePortMigration(string oldPortId, string? newPortId)
    {
        ArgumentNullException.ThrowIfNull(oldPortId);
        OldPortId = oldPortId;
        NewPortId = newPortId;
    }

    public string OldPortId { get; }

    public string? NewPortId { get; }
}

public sealed record ChangeInstanceContractIntent : EditIntent
{
    public ChangeInstanceContractIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<InstancePortMigration> ports,
        string? symbolVariantId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ArgumentNullException.ThrowIfNull(target);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        Target = target;
        Parameters = AuthoringInput.CopyRequiredReferences(parameters, nameof(parameters));
        Ports = AuthoringInput.CopyRequiredReferences(ports, nameof(ports));
        SymbolVariantId = symbolVariantId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public ComponentTarget Target { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ReadOnlyCollection<InstancePortMigration> Ports { get; }

    public string? SymbolVariantId { get; }
}

public sealed record RemoveComponentInstancesIntent : EditIntent
{
    public RemoveComponentInstancesIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<ComponentInstanceId> componentInstanceIds)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceIds = AuthoringInput.CopyRequiredReferences(
            componentInstanceIds,
            nameof(componentInstanceIds));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<ComponentInstanceId> ComponentInstanceIds { get; }
}

public sealed record CreateMemoryImageIntent : EditIntent
{
    public CreateMemoryImageIntent(
        string displayName,
        uint width,
        uint depth,
        IReadOnlyList<MemoryImageWord> words)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        DisplayName = displayName;
        Width = width;
        Depth = depth;
        Words = AuthoringInput.CopyRequiredReferences(words, nameof(words));
    }

    public string DisplayName { get; }

    public uint Width { get; }

    public uint Depth { get; }

    public ReadOnlyCollection<MemoryImageWord> Words { get; }
}

public sealed record InstanceParameterMigration
{
    public InstanceParameterMigration(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        Parameters = AuthoringInput.CopyRequiredReferences(parameters, nameof(parameters));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }
}

public sealed record ReplaceMemoryImageIntent : EditIntent
{
    public ReplaceMemoryImageIntent(
        MemoryImageId memoryImageId,
        string displayName,
        uint width,
        uint depth,
        IReadOnlyList<MemoryImageWord> words,
        IReadOnlyList<InstanceParameterMigration> affectedInstances)
    {
        ArgumentNullException.ThrowIfNull(memoryImageId);
        ArgumentNullException.ThrowIfNull(displayName);
        MemoryImageId = memoryImageId;
        DisplayName = displayName;
        Width = width;
        Depth = depth;
        Words = AuthoringInput.CopyRequiredReferences(words, nameof(words));
        AffectedInstances = AuthoringInput.CopyRequiredReferences(
            affectedInstances,
            nameof(affectedInstances));
    }

    public MemoryImageId MemoryImageId { get; }

    public string DisplayName { get; }

    public uint Width { get; }

    public uint Depth { get; }

    public ReadOnlyCollection<MemoryImageWord> Words { get; }

    public ReadOnlyCollection<InstanceParameterMigration> AffectedInstances { get; }
}

public sealed record RemoveMemoryImageIntent : EditIntent
{
    public RemoveMemoryImageIntent(MemoryImageId memoryImageId)
    {
        ArgumentNullException.ThrowIfNull(memoryImageId);
        MemoryImageId = memoryImageId;
    }

    public MemoryImageId MemoryImageId { get; }
}

public sealed record SymbolVariantMigration
{
    public SymbolVariantMigration(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        string? symbolVariantId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        SymbolVariantId = symbolVariantId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string? SymbolVariantId { get; }
}

public sealed record SetSymbolProfileIntent : EditIntent
{
    public SetSymbolProfileIntent(
        SymbolProfileReference symbolProfile,
        IReadOnlyList<SymbolVariantMigration> variants)
    {
        ArgumentNullException.ThrowIfNull(symbolProfile);
        SymbolProfile = symbolProfile;
        Variants = AuthoringInput.CopyRequiredReferences(variants, nameof(variants));
    }

    public SymbolProfileReference SymbolProfile { get; }

    public ReadOnlyCollection<SymbolVariantMigration> Variants { get; }
}

public sealed record SetSymbolVariantIntent : EditIntent
{
    public SetSymbolVariantIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        string? symbolVariantId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        CircuitDefinitionId = circuitDefinitionId;
        ComponentInstanceId = componentInstanceId;
        SymbolVariantId = symbolVariantId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string? SymbolVariantId { get; }
}

public sealed record CreateAnnotationIntent : EditIntent
{
    public CreateAnnotationIntent(
        CircuitDefinitionId circuitDefinitionId,
        AnnotationValue value)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(value);
        CircuitDefinitionId = circuitDefinitionId;
        Value = value;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public AnnotationValue Value { get; }
}

public sealed record ChangeAnnotationIntent : EditIntent
{
    public ChangeAnnotationIntent(
        CircuitDefinitionId circuitDefinitionId,
        AnnotationId annotationId,
        AnnotationValue value)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(annotationId);
        ArgumentNullException.ThrowIfNull(value);
        CircuitDefinitionId = circuitDefinitionId;
        AnnotationId = annotationId;
        Value = value;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public AnnotationId AnnotationId { get; }

    public AnnotationValue Value { get; }
}

public sealed record AnnotationMove
{
    public AnnotationMove(AnnotationId annotationId, GridPoint position)
    {
        ArgumentNullException.ThrowIfNull(annotationId);
        AnnotationId = annotationId;
        Position = position;
    }

    public AnnotationId AnnotationId { get; }

    public GridPoint Position { get; }
}

public sealed record MoveAnnotationsIntent : EditIntent
{
    public MoveAnnotationsIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<AnnotationMove> moves)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
        Moves = AuthoringInput.CopyRequiredReferences(moves, nameof(moves));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<AnnotationMove> Moves { get; }
}

public sealed record RemoveAnnotationIntent : EditIntent
{
    public RemoveAnnotationIntent(
        CircuitDefinitionId circuitDefinitionId,
        AnnotationId annotationId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(annotationId);
        CircuitDefinitionId = circuitDefinitionId;
        AnnotationId = annotationId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public AnnotationId AnnotationId { get; }
}
