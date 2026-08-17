using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.Scene;

internal static class SchematicGeometry
{
    public static PointV1 ToPlanPoint(
        GridPoint point,
        PresentationFingerprintV1 fingerprint) => new(
            checked(point.X * fingerprint.GridStepPlanUnits),
            checked(point.Y * fingerprint.GridStepPlanUnits));

    public static PointV1 Translate(PointV1 point, PointV1 origin) => new(
        checked(point.X + origin.X),
        checked(point.Y + origin.Y));

    public static RectV1 Translate(RectV1 bounds, PointV1 origin) => new(
        checked(bounds.Left + origin.X),
        checked(bounds.Top + origin.Y),
        checked(bounds.Right + origin.X),
        checked(bounds.Bottom + origin.Y));

    public static RectV1 Inflate(RectV1 bounds, int padding) => new(
        checked(bounds.Left - padding),
        checked(bounds.Top - padding),
        checked(bounds.Right + padding),
        checked(bounds.Bottom + padding));

    public static RectV1 CircleBounds(PointV1 center, int radius) => new(
        checked(center.X - radius),
        checked(center.Y - radius),
        checked(center.X + radius),
        checked(center.Y + radius));
}
