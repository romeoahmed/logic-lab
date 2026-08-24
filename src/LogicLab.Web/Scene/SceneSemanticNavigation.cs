using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;

namespace LogicLab.Web.Scene;

internal sealed record SceneSemanticNeighbors(
    string? Up,
    string? Down,
    string? Left,
    string? Right);

internal static class SceneSemanticNavigation
{
    public static IReadOnlyDictionary<string, SceneSemanticNeighbors> Project(
        AccessibleSceneProjection scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var builders = new Dictionary<string, NeighborBuilder>(StringComparer.Ordinal);

        var definitionPorts = scene.DefinitionPorts.Select(DefinitionPortSource).ToArray();
        var components = scene.Components.Select(ComponentSource).ToArray();
        var connections = scene.Connections.Select(NetSource).ToArray();
        var annotations = scene.Annotations.Select(AnnotationSource).ToArray();
        RegisterGroup(definitionPorts, builders);
        RegisterGroup(components, builders);
        RegisterGroup(connections, builders);
        RegisterGroup(annotations, builders);

        var terminalNets = new Dictionary<string, SceneSourceRefV1>(StringComparer.Ordinal);
        var terminalDirections = new Dictionary<string, PortDirection>(StringComparer.Ordinal);
        foreach (var port in scene.DefinitionPorts)
        {
            terminalDirections.Add(DefinitionPortSource(port).Key, port.Direction);
        }

        foreach (var port in scene.Components.SelectMany(component => component.Ports))
        {
            terminalDirections.Add(InstancePortSource(port).Key, port.Direction);
        }

        foreach (var connection in scene.Connections)
        {
            var net = NetSource(connection);
            var topology = connection.Junctions.Select(JunctionSource)
                .Concat(connection.WireGeometries.Select(WireSource))
                .ToArray();
            RegisterGroup(topology, builders);
            foreach (var item in topology)
            {
                builders[item.Key].Left = net.Key;
                builders[item.Key].Right = net.Key;
            }

            foreach (var terminal in connection.Terminals)
            {
                terminalNets.Add(TerminalSource(scene, terminal).Key, net);
            }
        }

        foreach (var component in scene.Components)
        {
            var source = ComponentSource(component);
            var ports = component.Ports.Select(InstancePortSource).ToArray();
            RegisterGroup(ports, builders);
            if (ports.Length > 0)
            {
                builders[source.Key].Left = ports[^1].Key;
                builders[source.Key].Right = ports[0].Key;
            }

            foreach (var (port, portSource) in component.Ports.Zip(ports))
            {
                terminalNets.TryGetValue(portSource.Key, out var net);
                var builder = builders[portSource.Key];
                if (port.Direction == PortDirection.Input)
                {
                    builder.Left = net?.Key;
                    builder.Right = source.Key;
                }
                else
                {
                    builder.Left = source.Key;
                    builder.Right = net?.Key;
                }
            }
        }

        foreach (var port in scene.DefinitionPorts)
        {
            var source = DefinitionPortSource(port);
            terminalNets.TryGetValue(source.Key, out var net);
            if (port.Direction == PortDirection.Output)
            {
                builders[source.Key].Left = net?.Key;
            }
            else
            {
                builders[source.Key].Right = net?.Key;
            }
        }

        foreach (var connection in scene.Connections)
        {
            var builder = builders[NetSource(connection).Key];
            var terminals = connection.Terminals.Select(terminal => new
            {
                Source = TerminalSource(scene, terminal),
                Terminal = terminal,
            }).ToArray();
            builder.Left = terminals.FirstOrDefault(terminal => IsDriver(
                    terminal.Terminal,
                    terminalDirections[terminal.Source.Key]))?.Source.Key
                ?? terminals.FirstOrDefault()?.Source.Key;
            builder.Right = terminals.FirstOrDefault(terminal => !IsDriver(
                    terminal.Terminal,
                    terminalDirections[terminal.Source.Key]))?.Source.Key
                ?? terminals.LastOrDefault()?.Source.Key;
        }

        return builders.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Build(),
            StringComparer.Ordinal);
    }

    private static void RegisterGroup(
        SceneSourceRefV1[] sources,
        IDictionary<string, NeighborBuilder> builders)
    {
        foreach (var source in sources)
        {
            builders.TryAdd(source.Key, new NeighborBuilder());
        }

        if (sources.Length < 2)
        {
            return;
        }

        for (var index = 0; index < sources.Length; index++)
        {
            var builder = builders[sources[index].Key];
            builder.Up = sources[(index + sources.Length - 1) % sources.Length].Key;
            builder.Down = sources[(index + 1) % sources.Length].Key;
        }
    }

    private static bool IsDriver(
        AuthoredTerminalReference terminal,
        PortDirection direction) => terminal switch
        {
            DefinitionTerminalReference => direction != PortDirection.Output,
            InstanceTerminalReference => direction != PortDirection.Input,
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        };

    private static SceneSourceRefV1 TerminalSource(
        AccessibleSceneProjection scene,
        AuthoredTerminalReference terminal) => terminal switch
        {
            DefinitionTerminalReference definition => new SceneSourceRefV1(
                scene.CircuitDefinitionId.Value,
                "definitionPort",
                definition.DefinitionPortId.Value),
            InstanceTerminalReference instance => new SceneSourceRefV1(
                scene.CircuitDefinitionId.Value,
                "instancePort",
                instance.ComponentInstanceId.Value,
                instance.PortId),
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        };

    private static SceneSourceRefV1 DefinitionPortSource(
        AccessibleDefinitionPortProjection port) => new(
            port.Source.CircuitDefinitionId.Value,
            "definitionPort",
            port.Source.DefinitionPortId.Value);

    private static SceneSourceRefV1 ComponentSource(AccessibleComponentProjection component) =>
        new(
            component.Source.CircuitDefinitionId.Value,
            "componentInstance",
            component.Source.ComponentInstanceId.Value);

    private static SceneSourceRefV1 InstancePortSource(AccessiblePortProjection port) => new(
        port.Source.CircuitDefinitionId.Value,
        "instancePort",
        port.Source.ComponentInstanceId.Value,
        port.Source.PortId);

    private static SceneSourceRefV1 NetSource(AccessibleConnectionProjection connection) => new(
        connection.Source.CircuitDefinitionId.Value,
        "net",
        connection.Source.NetId.Value);

    private static SceneSourceRefV1 JunctionSource(AccessibleJunctionProjection junction) => new(
        junction.Source.CircuitDefinitionId.Value,
        "junction",
        junction.Source.JunctionId.Value);

    private static SceneSourceRefV1 WireSource(AccessibleWireGeometryProjection wire) => new(
        wire.Source.CircuitDefinitionId.Value,
        "wireGeometry",
        wire.Source.WireGeometryId.Value);

    private static SceneSourceRefV1 AnnotationSource(
        AccessibleAnnotationProjection annotation) => new(
            annotation.Source.CircuitDefinitionId.Value,
            "annotation",
            annotation.Source.AnnotationId.Value);

    private sealed class NeighborBuilder
    {
        public string? Up { get; set; }

        public string? Down { get; set; }

        public string? Left { get; set; }

        public string? Right { get; set; }

        public SceneSemanticNeighbors Build() => new(Up, Down, Left, Right);
    }
}
