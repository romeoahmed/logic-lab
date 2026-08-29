namespace LogicLab.Web.Scene;

internal static class SceneWireRouting
{
    public static SceneWireRouteV1 Between(
        SceneSnapshotV1? snapshot,
        SceneSourceRefV1 startSource,
        SceneSourceRefV1 endSource)
    {
        ArgumentNullException.ThrowIfNull(startSource);
        ArgumentNullException.ThrowIfNull(endSource);
        if (snapshot is null
            || !TryResolveEndpoint(snapshot, startSource, out var start)
            || !TryResolveEndpoint(snapshot, endSource, out var end)
            || start.Point == end.Point)
        {
            return new SceneUnroutedWireRouteV1();
        }

        try
        {
            var points = BuildOrthogonalPoints(
                start,
                end,
                snapshot.SnapStepGridUnits);
            return points.Count >= 2
                ? new SceneOrthogonalWireRouteV1(points)
                : new SceneUnroutedWireRouteV1();
        }
        catch (OverflowException)
        {
            return new SceneUnroutedWireRouteV1();
        }
    }

    private static bool TryResolveEndpoint(
        SceneSnapshotV1 snapshot,
        SceneSourceRefV1 source,
        out RouteEndpoint endpoint)
    {
        foreach (var item in snapshot.Items)
        {
            foreach (var region in item.HitRegions)
            {
                if (region.TargetSource is { } target
                    && target.Key == source.Key
                    && region.Anchor is { } anchor
                    && TryGridPoint(
                        snapshot,
                        new ScenePoint(
                            anchor.X + item.Origin.X,
                            anchor.Y + item.Origin.Y),
                        out var point))
                {
                    endpoint = new RouteEndpoint(
                        point,
                        Direction(region.OutwardDirection));
                    return true;
                }
            }

            if (item.Source.Key != source.Key || item.HitRegions.Count == 0)
            {
                continue;
            }

            var firstRegion = item.HitRegions[0];
            var localCenter = firstRegion.Center ?? new ScenePoint(
                (firstRegion.Bounds.Left + firstRegion.Bounds.Right) / 2,
                (firstRegion.Bounds.Top + firstRegion.Bounds.Bottom) / 2);
            if (TryGridPoint(
                    snapshot,
                    new ScenePoint(
                        localCenter.X + item.Origin.X,
                        localCenter.Y + item.Origin.Y),
                    out var genericPoint))
            {
                endpoint = new RouteEndpoint(genericPoint, null);
                return true;
            }
        }

        endpoint = default;
        return false;
    }

    private static bool TryGridPoint(
        SceneSnapshotV1 snapshot,
        ScenePoint point,
        out SceneGridPointV1 gridPoint)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            gridPoint = null!;
            return false;
        }

        var x = Snap(point.X, snapshot.GridStepPlanUnits, snapshot.SnapStepGridUnits);
        var y = Snap(point.Y, snapshot.GridStepPlanUnits, snapshot.SnapStepGridUnits);
        if (x is < int.MinValue or > int.MaxValue
            || y is < int.MinValue or > int.MaxValue)
        {
            gridPoint = null!;
            return false;
        }

        gridPoint = new SceneGridPointV1((int)x, (int)y);
        return true;
    }

    private static long Snap(double coordinate, int gridStep, int snapStep)
    {
        var integerGridCoordinate = RoundHalfNegativeInfinity(coordinate / gridStep);
        return checked(RoundHalfNegativeInfinity(
            integerGridCoordinate / (double)snapStep) * snapStep);
    }

    private static long RoundHalfNegativeInfinity(double value) => checked(
        (long)Math.Ceiling(value - 0.5));

    private static List<SceneGridPointV1> BuildOrthogonalPoints(
        RouteEndpoint start,
        RouteEndpoint end,
        int lead)
    {
        if (CanRouteDirectly(start, end))
        {
            return [start.Point, end.Point];
        }

        var step = Math.Max(1, lead);
        var startLead = Offset(start.Point, start.Direction, step);
        var endLead = Offset(end.Point, end.Direction, step);
        var points = new List<SceneGridPointV1> { start.Point, startLead };
        var startIsHorizontal = start.Direction is { X: not 0 };
        var endIsHorizontal = end.Direction is { X: not 0 };
        var startIsVertical = start.Direction is { Y: not 0 };
        var endIsVertical = end.Direction is { Y: not 0 };

        if (startLead.X == endLead.X || startLead.Y == endLead.Y)
        {
            points.Add(endLead);
        }
        else if (startIsHorizontal && endIsHorizontal)
        {
            var delta = checked(end.Point.X - (long)start.Point.X);
            var faceEachOther = Math.Sign(delta) == start.Direction!.Value.X
                && Math.Sign(-delta) == end.Direction!.Value.X
                && Math.Abs(delta) >= step * 2L;
            var channelX = faceEachOther
                ? SnapMidpoint(startLead.X, endLead.X, step)
                : start.Direction.Value.X > 0
                    ? checked(Math.Max(startLead.X, endLead.X) + step)
                    : checked(Math.Min(startLead.X, endLead.X) - step);
            points.Add(new SceneGridPointV1(channelX, startLead.Y));
            points.Add(new SceneGridPointV1(channelX, endLead.Y));
            points.Add(endLead);
        }
        else if (startIsVertical && endIsVertical)
        {
            var delta = checked(end.Point.Y - (long)start.Point.Y);
            var faceEachOther = Math.Sign(delta) == start.Direction!.Value.Y
                && Math.Sign(-delta) == end.Direction!.Value.Y
                && Math.Abs(delta) >= step * 2L;
            var channelY = faceEachOther
                ? SnapMidpoint(startLead.Y, endLead.Y, step)
                : start.Direction.Value.Y > 0
                    ? checked(Math.Max(startLead.Y, endLead.Y) + step)
                    : checked(Math.Min(startLead.Y, endLead.Y) - step);
            points.Add(new SceneGridPointV1(startLead.X, channelY));
            points.Add(new SceneGridPointV1(endLead.X, channelY));
            points.Add(endLead);
        }
        else if (endIsHorizontal)
        {
            points.Add(new SceneGridPointV1(startLead.X, endLead.Y));
            points.Add(endLead);
        }
        else
        {
            points.Add(new SceneGridPointV1(endLead.X, startLead.Y));
            points.Add(endLead);
        }

        points.Add(end.Point);
        return Compact(points);
    }

    private static bool CanRouteDirectly(RouteEndpoint start, RouteEndpoint end)
    {
        if (start.Point.Y == end.Point.Y)
        {
            var direction = Math.Sign(end.Point.X - (long)start.Point.X);
            return Allows(start.Direction, direction, horizontal: true)
                && Allows(end.Direction, -direction, horizontal: true);
        }

        if (start.Point.X == end.Point.X)
        {
            var direction = Math.Sign(end.Point.Y - (long)start.Point.Y);
            return Allows(start.Direction, direction, horizontal: false)
                && Allows(end.Direction, -direction, horizontal: false);
        }

        return false;
    }

    private static bool Allows(DirectionVector? direction, int sign, bool horizontal) =>
        direction is null
        || (horizontal
            ? direction.Value.Y == 0 && direction.Value.X == sign
            : direction.Value.X == 0 && direction.Value.Y == sign);

    private static SceneGridPointV1 Offset(
        SceneGridPointV1 point,
        DirectionVector? direction,
        int distance) => direction is null
            ? point
            : new SceneGridPointV1(
                checked(point.X + (direction.Value.X * distance)),
                checked(point.Y + (direction.Value.Y * distance)));

    private static int SnapMidpoint(int first, int second, int step)
    {
        var midpoint = (first + (double)second) / 2;
        return checked((int)(RoundHalfNegativeInfinity(midpoint / step) * step));
    }

    private static List<SceneGridPointV1> Compact(
        IEnumerable<SceneGridPointV1> points)
    {
        var compacted = new List<SceneGridPointV1>();
        foreach (var point in points)
        {
            if (compacted.Count > 0 && compacted[^1] == point)
            {
                continue;
            }

            while (compacted.Count >= 2)
            {
                var previous = compacted[^2];
                var current = compacted[^1];
                if ((previous.X == current.X && current.X == point.X)
                    || (previous.Y == current.Y && current.Y == point.Y))
                {
                    compacted.RemoveAt(compacted.Count - 1);
                }
                else
                {
                    break;
                }
            }

            compacted.Add(point);
        }

        return compacted;
    }

    private static DirectionVector? Direction(string? value) => value switch
    {
        "north" => new(0, -1),
        "east" => new(1, 0),
        "south" => new(0, 1),
        "west" => new(-1, 0),
        _ => null,
    };

    private readonly record struct RouteEndpoint(
        SceneGridPointV1 Point,
        DirectionVector? Direction);

    private readonly record struct DirectionVector(int X, int Y);
}
