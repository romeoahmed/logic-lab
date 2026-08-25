namespace LogicLab.Web.Scene;

internal readonly record struct ScenePoint(double X, double Y);

internal readonly record struct SceneRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;
}
