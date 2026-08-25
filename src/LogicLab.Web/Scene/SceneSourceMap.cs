using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Scene;

namespace LogicLab.Web.Scene;

internal static class SceneSourceMap
{
    public static IEnumerable<SceneSourceRefV1> Enumerate(AccessibleSceneProjection scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (var port in scene.DefinitionPorts)
        {
            yield return From(port);
        }

        foreach (var component in scene.Components)
        {
            yield return From(component);
            foreach (var port in component.Ports)
            {
                yield return From(port);
            }
        }

        foreach (var annotation in scene.Annotations)
        {
            yield return From(annotation);
        }

        foreach (var connection in scene.Connections)
        {
            yield return From(connection);
            foreach (var junction in connection.Junctions)
            {
                yield return From(junction);
            }

            foreach (var wire in connection.WireGeometries)
            {
                yield return From(wire);
            }
        }
    }

    public static SceneSourceRefV1 From(AccessibleDefinitionPortProjection port) =>
        From(port.Source);

    public static SceneSourceRefV1 From(AccessibleComponentProjection component) =>
        From(component.Source);

    public static SceneSourceRefV1 From(AccessiblePortProjection port) => From(port.Source);

    public static SceneSourceRefV1 From(AccessibleConnectionProjection connection) =>
        From(connection.Source);

    public static SceneSourceRefV1 From(AccessibleJunctionProjection junction) =>
        From(junction.Source);

    public static SceneSourceRefV1 From(AccessibleWireGeometryProjection wire) =>
        From(wire.Source);

    public static SceneSourceRefV1 From(AccessibleAnnotationProjection annotation) =>
        From(annotation.Source);

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
