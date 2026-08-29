using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Scene;

internal static class SceneSourceMap
{
    public static bool Contains(ProjectRevision revision, SceneSourceRefV1 source)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (source is null)
        {
            return false;
        }

        var definition = revision.Document.CircuitDefinitions.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id.Value,
                source.CircuitDefinitionId,
                StringComparison.Ordinal));
        if (definition is null)
        {
            return false;
        }

        return source.EntityKind switch
        {
            "definitionPort" => HasNoPort(source)
                && definition.Ports.Any(port => HasId(port.Id.Value, source)),
            "componentInstance" => HasNoPort(source)
                && definition.ComponentInstances.Any(instance =>
                    HasId(instance.Id.Value, source)),
            "instancePort" => source.PortId is { Length: > 0 } portId
                && definition.ComponentInstances.FirstOrDefault(instance =>
                    HasId(instance.Id.Value, source)) is { } instance
                && ContainsPort(revision, instance, portId),
            "net" => HasNoPort(source)
                && definition.Nets.Any(net => HasId(net.Id.Value, source)),
            "junction" => HasNoPort(source)
                && definition.Junctions.Any(junction => HasId(junction.Id.Value, source)),
            "wireGeometry" => HasNoPort(source)
                && definition.WireGeometries.Any(geometry =>
                    HasId(geometry.Id.Value, source)),
            "annotation" => HasNoPort(source)
                && definition.Annotations.Any(annotation =>
                    HasId(annotation.Id.Value, source)),
            _ => false,
        };
    }

    private static bool ContainsPort(
        ProjectRevision revision,
        ComponentInstance instance,
        string portId)
    {
        return instance.Target switch
        {
            LibraryComponentTarget library => revision.Document.LibrarySnapshot
                .ResolveContract(library.ContractKey)?
                .TryResolvePort(instance.Parameters, portId, out _) is true,
            CircuitDefinitionComponentTarget target => revision.Document
                .FindCircuitDefinition(target.CircuitDefinitionId)?
                .Ports.Any(port => string.Equals(
                    port.Id.Value,
                    portId,
                    StringComparison.Ordinal)) is true,
            _ => false,
        };
    }

    private static bool HasId(string candidate, SceneSourceRefV1 source) =>
        string.Equals(candidate, source.EntityId, StringComparison.Ordinal);

    private static bool HasNoPort(SceneSourceRefV1 source) => source.PortId is null;

    public static SceneSourceRefV1 From(DefinitionPortSourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "definitionPort",
        source.DefinitionPortId.Value);

    public static SceneSourceRefV1 From(ComponentInstanceSourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "componentInstance",
        source.ComponentInstanceId.Value);

    public static SceneSourceRefV1 From(InstancePortSourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "instancePort",
        source.ComponentInstanceId.Value,
        source.PortId);

    public static SceneSourceRefV1 From(NetSourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "net",
        source.NetId.Value);

    public static SceneSourceRefV1 From(JunctionSourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "junction",
        source.JunctionId.Value);

    public static SceneSourceRefV1 From(WireGeometrySourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "wireGeometry",
        source.WireGeometryId.Value);

    public static SceneSourceRefV1 From(AnnotationSourceIdentity source) => new(
        source.CircuitDefinitionId.Value,
        "annotation",
        source.AnnotationId.Value);

    public static SceneSourceRefV1 From(
        CircuitDefinitionId circuitDefinitionId,
        AuthoredTerminalReference terminal) => terminal switch
        {
            DefinitionTerminalReference definition => new SceneSourceRefV1(
                circuitDefinitionId.Value,
                "definitionPort",
                definition.DefinitionPortId.Value),
            InstanceTerminalReference instance => new SceneSourceRefV1(
                circuitDefinitionId.Value,
                "instancePort",
                instance.ComponentInstanceId.Value,
                instance.PortId),
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        };

    public static SceneSourceRefV1? TryFrom(AuthoredSourceIdentity identity) => identity switch
    {
        DefinitionPortSourceIdentity source => From(source),
        ComponentInstanceSourceIdentity source => From(source),
        InstancePortSourceIdentity source => From(source),
        NetSourceIdentity source => From(source),
        JunctionSourceIdentity source => From(source),
        WireGeometrySourceIdentity source => From(source),
        AnnotationSourceIdentity source => From(source),
        _ => null,
    };
}
