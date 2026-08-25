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

        var definitionPorts = scene.DefinitionPorts.Select(SceneSourceMap.From).ToArray();
        var components = scene.Components.Select(SceneSourceMap.From).ToArray();
        var connections = scene.Connections.Select(SceneSourceMap.From).ToArray();
        var annotations = scene.Annotations.Select(SceneSourceMap.From).ToArray();
        RegisterGroup(definitionPorts, builders);
        RegisterGroup(components, builders);
        RegisterGroup(connections, builders);
        RegisterGroup(annotations, builders);

        var terminalNets = new Dictionary<string, SceneSourceRefV1>(StringComparer.Ordinal);
        var terminalDirections = new Dictionary<string, PortDirection>(StringComparer.Ordinal);
        foreach (var port in scene.DefinitionPorts)
        {
            terminalDirections.Add(SceneSourceMap.From(port).Key, port.Direction);
        }

        foreach (var port in scene.Components.SelectMany(component => component.Ports))
        {
            terminalDirections.Add(SceneSourceMap.From(port).Key, port.Direction);
        }

        foreach (var connection in scene.Connections)
        {
            var net = SceneSourceMap.From(connection);
            var topology = connection.Junctions.Select(SceneSourceMap.From)
                .Concat(connection.WireGeometries.Select(SceneSourceMap.From))
                .ToArray();
            RegisterGroup(topology, builders);
            foreach (var item in topology)
            {
                builders[item.Key].Left = net.Key;
                builders[item.Key].Right = net.Key;
            }

            foreach (var terminal in connection.Terminals)
            {
                terminalNets.Add(SceneSourceMap.From(scene.CircuitDefinitionId, terminal).Key, net);
            }
        }

        foreach (var component in scene.Components)
        {
            var source = SceneSourceMap.From(component);
            var ports = component.Ports.Select(SceneSourceMap.From).ToArray();
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
            var source = SceneSourceMap.From(port);
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
            var builder = builders[SceneSourceMap.From(connection).Key];
            var terminals = connection.Terminals.Select(terminal => new
            {
                Source = SceneSourceMap.From(scene.CircuitDefinitionId, terminal),
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

    private sealed class NeighborBuilder
    {
        public string? Up { get; set; }

        public string? Down { get; set; }

        public string? Left { get; set; }

        public string? Right { get; set; }

        public SceneSemanticNeighbors Build() => new(Up, Down, Left, Right);
    }
}
