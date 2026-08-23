using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal static class TeachingMixedQualifierGeometry
{
    private static readonly LineJoinV1 MiterJoin = new(LineJoinKindV1.Miter, 4);

    public static StrokePathV1 Circle(PointV1 center, int radius, int width)
    {
        var curve = Math.Max(1, checked(radius * 552 / 1000));
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(checked(center.X + radius), center.Y)),
                new CubicToV1(
                    new PointV1(checked(center.X + radius), checked(center.Y + curve)),
                    new PointV1(checked(center.X + curve), checked(center.Y + radius)),
                    new PointV1(center.X, checked(center.Y + radius))),
                new CubicToV1(
                    new PointV1(checked(center.X - curve), checked(center.Y + radius)),
                    new PointV1(checked(center.X - radius), checked(center.Y + curve)),
                    new PointV1(checked(center.X - radius), center.Y)),
                new CubicToV1(
                    new PointV1(checked(center.X - radius), checked(center.Y - curve)),
                    new PointV1(checked(center.X - curve), checked(center.Y - radius)),
                    new PointV1(center.X, checked(center.Y - radius))),
                new CubicToV1(
                    new PointV1(checked(center.X + curve), checked(center.Y - radius)),
                    new PointV1(checked(center.X + radius), checked(center.Y - curve)),
                    new PointV1(checked(center.X + radius), center.Y)),
                new ClosePathV1(),
            ]),
            width);
    }

    public static StrokePathV1 DirectPolarityInput(
        int bodyLeft,
        int centerY,
        int h,
        int width)
    {
        var baseX = checked(bodyLeft - h);
        var halfHeight = ScaleUp(h, 1, 2);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(baseX, checked(centerY - halfHeight))),
                new LineToV1(new PointV1(bodyLeft, centerY)),
                new LineToV1(new PointV1(baseX, checked(centerY + halfHeight))),
                new ClosePathV1(),
            ]),
            width);
    }

    public static StrokePathV1 DirectPolarityOutput(
        int bodyRight,
        int centerY,
        int h,
        int width)
    {
        var tipX = checked(bodyRight + h);
        var halfHeight = ScaleUp(h, 1, 2);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(bodyRight, checked(centerY - halfHeight))),
                new LineToV1(new PointV1(tipX, centerY)),
                new LineToV1(new PointV1(bodyRight, checked(centerY + halfHeight))),
                new ClosePathV1(),
            ]),
            width);
    }

    public static StrokePathV1 DynamicInput(
        int bodyLeft,
        int centerY,
        int h,
        int width)
    {
        var halfHeight = ScaleUp(h, 1, 2);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(bodyLeft, checked(centerY - halfHeight))),
                new LineToV1(new PointV1(checked(bodyLeft + h), centerY)),
                new LineToV1(new PointV1(bodyLeft, checked(centerY + halfHeight))),
            ]),
            width);
    }

    public static StrokePathV1 ThreeStateOutput(
        int bodyRight,
        int centerY,
        int radius,
        int width)
    {
        var left = checked(bodyRight - (2 * radius));
        var centerX = checked(bodyRight - radius);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(left, centerY)),
                new LineToV1(new PointV1(bodyRight, centerY)),
                new LineToV1(new PointV1(centerX, checked(centerY + radius))),
                new ClosePathV1(),
            ]),
            width);
    }

    public static StrokePathV1 BitGroupingInputBrace(
        int left,
        int right,
        int centerY,
        int halfHeight,
        int width)
    {
        var shoulderX = checked(left + ((right - left) * 2 / 3));
        var upperShoulderY = checked(centerY - (halfHeight / 3));
        var lowerShoulderY = checked(centerY + (halfHeight / 3));
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(left, checked(centerY - halfHeight))),
                new CubicToV1(
                    new PointV1(shoulderX, checked(centerY - halfHeight)),
                    new PointV1(shoulderX, checked(centerY - (2 * halfHeight / 3))),
                    new PointV1(shoulderX, upperShoulderY)),
                new CubicToV1(
                    new PointV1(shoulderX, checked(centerY - (halfHeight / 6))),
                    new PointV1(right, checked(centerY - (halfHeight / 6))),
                    new PointV1(right, centerY)),
                new CubicToV1(
                    new PointV1(right, checked(centerY + (halfHeight / 6))),
                    new PointV1(shoulderX, checked(centerY + (halfHeight / 6))),
                    new PointV1(shoulderX, lowerShoulderY)),
                new CubicToV1(
                    new PointV1(shoulderX, checked(centerY + (2 * halfHeight / 3))),
                    new PointV1(shoulderX, checked(centerY + halfHeight)),
                    new PointV1(left, checked(centerY + halfHeight))),
            ]),
            width);
    }

    private static StrokePathV1 Stroke(PathV1 path, int width) => new(
        path,
        StrokeRoleV1.Qualifier,
        width,
        [],
        LineCapV1.Butt,
        MiterJoin);

    private static int ScaleUp(int value, int numerator, int denominator) =>
        checked((int)((((long)value * numerator) + denominator - 1) / denominator));
}
