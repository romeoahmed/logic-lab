namespace LogicLab.Domain.Authoring;

public abstract record AuthoredSourceIdentity
{
    private protected AuthoredSourceIdentity()
    {
    }
}

public sealed record ProjectRootSourceIdentity(ProjectId ProjectId)
    : AuthoredSourceIdentity;

public sealed record MemoryImageSourceIdentity(
    ProjectId ProjectId,
    MemoryImageId MemoryImageId) : AuthoredSourceIdentity;

// Circuit-scoped identities carry their container once, independently of hierarchy occurrences.
public abstract record CircuitSourceIdentity : AuthoredSourceIdentity
{
    private protected CircuitSourceIdentity(CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }
}

public sealed record CircuitRootSourceIdentity(CircuitDefinitionId CircuitDefinitionId)
    : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record DefinitionPortSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    DefinitionPortId DefinitionPortId) : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record ComponentInstanceSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId) : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record InstancePortSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId,
    string PortId) : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record NetSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    NetId NetId) : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record JunctionSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    JunctionId JunctionId) : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record WireGeometrySourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    WireGeometryId WireGeometryId) : CircuitSourceIdentity(CircuitDefinitionId);

public sealed record AnnotationSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    AnnotationId AnnotationId) : CircuitSourceIdentity(CircuitDefinitionId);
