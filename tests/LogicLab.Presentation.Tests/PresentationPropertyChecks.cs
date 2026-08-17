using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.Tests;

internal static class PresentationPropertyChecks
{
    public static void Check(bool condition, string message, List<string> violations)
    {
        if (!condition)
        {
            violations.Add(message);
        }
    }

    public static bool Contains(RectV1 outer, RectV1 inner) =>
        inner.Left >= outer.Left
        && inner.Top >= outer.Top
        && inner.Right <= outer.Right
        && inner.Bottom <= outer.Bottom;

    public static bool PortHitRegionsAreDisjoint(GeometryPlanV1 plan)
    {
        var circles = plan.HitRegions
            .Where(region => region.Kind == HitRegionKindV1.Port)
            .Select(region => region.Shape)
            .OfType<CircleHitShapeV1>()
            .ToArray();
        if (circles.Length != plan.PortAnchors.Count)
        {
            return false;
        }

        for (var first = 0; first < circles.Length; first++)
        {
            for (var second = first + 1; second < circles.Length; second++)
            {
                var deltaX = (long)circles[first].Center.X - circles[second].Center.X;
                var deltaY = (long)circles[first].Center.Y - circles[second].Center.Y;
                var radiusSum = (long)circles[first].Radius + circles[second].Radius;
                if ((deltaX * deltaX) + (deltaY * deltaY) <= radiusSum * radiusSum)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
