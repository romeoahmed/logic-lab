namespace LogicLab.Web.Scene;

public readonly record struct ScenePoint(double X, double Y);

public readonly record struct SceneRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool Intersects(SceneRect other) =>
        Left <= other.Right
        && Right >= other.Left
        && Top <= other.Bottom
        && Bottom >= other.Top;
}

public sealed record SceneViewport
{
    public SceneViewport(
        double translateX,
        double translateY,
        double zoom,
        double minimumZoom,
        double maximumZoom)
    {
        if (!double.IsFinite(translateX)
            || !double.IsFinite(translateY)
            || !double.IsFinite(zoom)
            || !double.IsFinite(minimumZoom)
            || !double.IsFinite(maximumZoom)
            || minimumZoom <= 0
            || maximumZoom < minimumZoom
            || zoom < minimumZoom
            || zoom > maximumZoom)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        TranslateX = translateX;
        TranslateY = translateY;
        Zoom = zoom;
        MinimumZoom = minimumZoom;
        MaximumZoom = maximumZoom;
    }

    public double TranslateX { get; }

    public double TranslateY { get; }

    public double Zoom { get; }

    public double MinimumZoom { get; }

    public double MaximumZoom { get; }

    public ScenePoint WorldToScreen(ScenePoint world)
    {
        EnsureFinite(world, nameof(world));
        var screen = new ScenePoint(
            (world.X * Zoom) + TranslateX,
            (world.Y * Zoom) + TranslateY);
        EnsureFinite(screen, nameof(world));
        return screen;
    }

    public ScenePoint ScreenToWorld(ScenePoint screen)
    {
        EnsureFinite(screen, nameof(screen));
        var world = new ScenePoint(
            (screen.X - TranslateX) / Zoom,
            (screen.Y - TranslateY) / Zoom);
        EnsureFinite(world, nameof(screen));
        return world;
    }

    public SceneViewport ZoomAt(ScenePoint screenAnchor, double requestedZoom)
    {
        if (!double.IsFinite(requestedZoom))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedZoom));
        }

        var worldAnchor = ScreenToWorld(screenAnchor);
        var zoom = Math.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
        return new SceneViewport(
            screenAnchor.X - (worldAnchor.X * zoom),
            screenAnchor.Y - (worldAnchor.Y * zoom),
            zoom,
            MinimumZoom,
            MaximumZoom);
    }

    public static int CommitCoordinate(
        double planCoordinate,
        int gridStepPlanUnits,
        int snapStepGridUnits,
        bool disableSnap)
    {
        if (!double.IsFinite(planCoordinate))
        {
            throw new ArgumentOutOfRangeException(nameof(planCoordinate));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridStepPlanUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapStepGridUnits);
        var grid = RoundHalfTowardNegativeInfinity(planCoordinate / gridStepPlanUnits);
        if (grid is < int.MinValue or > int.MaxValue)
        {
            throw new OverflowException("The committed grid coordinate exceeds Int32.");
        }

        return disableSnap ? (int)grid : SnapCoordinate((int)grid, snapStepGridUnits);
    }

    public static int SnapCoordinate(int coordinate, int snapStepGridUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapStepGridUnits);
        var quotient = (double)coordinate / snapStepGridUnits;
        var snapped = checked(RoundHalfTowardNegativeInfinity(quotient) * snapStepGridUnits);
        if (snapped is < int.MinValue or > int.MaxValue)
        {
            throw new OverflowException("The snapped grid coordinate exceeds Int32.");
        }

        return (int)snapped;
    }

    private static long RoundHalfTowardNegativeInfinity(double value)
    {
        var floor = Math.Floor(value);
        var fraction = value - floor;
        return checked((long)(fraction <= 0.5 ? floor : floor + 1));
    }

    private static void EnsureFinite(ScenePoint point, string parameterName)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Scene coordinates must be finite.");
        }
    }
}
