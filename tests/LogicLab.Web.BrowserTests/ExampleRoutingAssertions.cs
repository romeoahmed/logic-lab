using LogicLab.Domain.Authoring;
using LogicLab.Web.Scene;

namespace LogicLab.Web.BrowserTests;

internal static class ExampleRoutingAssertions
{
    public static async Task VerifyAsync(CircuitDefinition definition, SceneSnapshotV1 scene)
    {
        var scale = scene.GridStepPlanUnits;
        var errors = new List<string>();
        var edges = new Dictionary<(ScenePoint, ScenePoint), string>();
        var vertices = new Dictionary<ScenePoint, Dictionary<string, HashSet<ScenePoint>>>();
        var obstacles = scene.Items.Where(item => item.Source.EntityKind == "componentInstance")
            .SelectMany(item => item.HitRegions
            .Where(hit => hit.Kind == "body")
            .Select(hit => (item.Source.EntityId, Bounds: Translate(hit.Bounds, item.Origin))))
            .Concat(scene.Items.Where(item => item.Source.EntityKind == "annotation")
                .Select(item => (item.Source.EntityId, Bounds: Translate(item.Bounds, item.Origin))))
            .ToArray();
        foreach (var geometry in definition.WireGeometries)
        {
            if (geometry.Route is not OrthogonalWireRoute route)
            {
                errors.Add($"Unrouted wire {geometry.Id.Value}.");
                continue;
            }

            var netId = geometry.NetId.Value;
            for (var index = 1; index < route.Points.Count; index++)
            {
                var start = route.Points[index - 1];
                var end = route.Points[index];
                var a = new ScenePoint(start.X * scale, start.Y * scale);
                var b = new ScenePoint(end.X * scale, end.Y * scale);
                foreach (var obstacle in obstacles.Where(obstacle => Intersects(a, b, obstacle.Bounds)))
                {
                    errors.Add($"Wire {geometry.Id.Value} crosses body or label {obstacle.EntityId}.");
                }

                var dx = Math.Sign(end.X - start.X);
                var dy = Math.Sign(end.Y - start.Y);
                var point = new ScenePoint(start.X, start.Y);
                var target = new ScenePoint(end.X, end.Y);
                while (point != target)
                {
                    var next = new ScenePoint(point.X + dx, point.Y + dy);
                    var key = point.X < next.X || point.Y < next.Y ? (point, next) : (next, point);
                    if (!edges.TryAdd(key, netId))
                    {
                        errors.Add($"Overlapping wire segment at {point}.");
                    }

                    AddNeighbor(point, next, netId);
                    AddNeighbor(next, point, netId);
                    point = next;
                }
            }
        }

        foreach (var (point, nets) in vertices)
        {
            if (nets.Count > 1)
            {
                var axes = nets.Values.Select(neighbors => StraightAxis(point, neighbors)).ToArray();
                if (axes.Length != 2 || axes.Contains(-1) || axes[0] == axes[1])
                {
                    errors.Add($"Ambiguous crossing between different Nets at {point}.");
                }
            }

            foreach (var (netId, neighbors) in nets.Where(pair => pair.Value.Count > 2))
            {
                if (!definition.Junctions.Any(junction => junction.NetId.Value == netId
                    && junction.Position.X == point.X && junction.Position.Y == point.Y))
                {
                    errors.Add($"Missing explicit Junction at branch {point}.");
                }
            }
        }

        var anchors = scene.Items.SelectMany(item => item.HitRegions
            .Where(hit => hit.TargetSource is { EntityKind: "instancePort" } && hit.Anchor is not null)
            .Select(hit => KeyValuePair.Create(
                (hit.TargetSource!.EntityId, hit.TargetSource.PortId!),
                new ScenePoint((hit.Anchor!.Value.X + item.Origin.X) / scale,
                    (hit.Anchor.Value.Y + item.Origin.Y) / scale))))
            .ToDictionary();
        foreach (var net in definition.Nets)
        {
            var terminals = net.Terminals.Cast<InstanceTerminalReference>()
                .Select(terminal => anchors[(terminal.ComponentInstanceId.Value, terminal.PortId)])
                .ToHashSet();
            var reached = new HashSet<ScenePoint>();
            var pending = new Stack<ScenePoint>();
            pending.Push(terminals.First());
            while (pending.TryPop(out var point))
            {
                if (!reached.Add(point) || !vertices.TryGetValue(point, out var nets)
                    || !nets.TryGetValue(net.Id.Value, out var neighbors))
                {
                    continue;
                }

                foreach (var neighbor in neighbors)
                {
                    pending.Push(neighbor);
                }
            }

            if (terminals.Any(point => !vertices.TryGetValue(point, out var nets)
                || !nets.TryGetValue(net.Id.Value, out var neighbors) || neighbors.Count != 1)
                || vertices.Any(pair => pair.Value.ContainsKey(net.Id.Value) && !reached.Contains(pair.Key)))
            {
                errors.Add($"Net {net.Id.Value} has disconnected geometry or a missing terminal lead.");
            }
        }

        foreach (var junction in definition.Junctions)
        {
            var point = new ScenePoint(junction.Position.X, junction.Position.Y);
            if (!vertices.TryGetValue(point, out var nets)
                || !nets.TryGetValue(junction.NetId.Value, out var neighbors) || neighbors.Count < 3)
            {
                errors.Add($"Junction {junction.Id.Value} does not mark a wire branch.");
            }
        }

        await Assert.That(errors).IsEmpty();

        void AddNeighbor(ScenePoint point, ScenePoint neighbor, string netId)
        {
            if (!vertices.TryGetValue(point, out var nets))
            {
                vertices.Add(point, nets = []);
            }

            if (!nets.TryGetValue(netId, out var neighbors))
            {
                nets.Add(netId, neighbors = []);
            }

            neighbors.Add(neighbor);
        }
    }

    private static int StraightAxis(ScenePoint point, HashSet<ScenePoint> neighbors) =>
        neighbors.Count != 2 ? -1
        : neighbors.All(next => next.X == point.X) ? 1
        : neighbors.All(next => next.Y == point.Y) ? 0
        : -1;

    private static SceneRect Translate(SceneRect bounds, ScenePoint origin) => new(
        bounds.Left + origin.X, bounds.Top + origin.Y,
        bounds.Right + origin.X, bounds.Bottom + origin.Y);

    private static bool Intersects(ScenePoint a, ScenePoint b, SceneRect bounds) =>
        Math.Max(a.X, b.X) >= bounds.Left && Math.Min(a.X, b.X) <= bounds.Right
        && Math.Max(a.Y, b.Y) >= bounds.Top && Math.Min(a.Y, b.Y) <= bounds.Bottom;
}
