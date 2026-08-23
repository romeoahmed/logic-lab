using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SceneViewportTests
{
    [Test]
    [Arguments(0d, 0d)]
    [Arguments(-120.5d, 87.25d)]
    [Arguments(2_147_000_000d, -2_147_000_000d)]
    public async Task Transform_WorldScreenRoundTrip_PreservesCoordinates(double x, double y)
    {
        var viewport = new SceneViewport(31.25, -18.75, 1.75, 0.25, 4);

        var screen = viewport.WorldToScreen(new ScenePoint(x, y));
        var world = viewport.ScreenToWorld(screen);

        using (Assert.Multiple())
        {
            await Assert.That(world.X).IsEqualTo(x).Within(0.000_001);
            await Assert.That(world.Y).IsEqualTo(y).Within(0.000_001);
        }
    }

    [Test]
    [Arguments(50d, 0)]
    [Arguments(-50d, -1)]
    [Arguments(150d, 1)]
    [Arguments(-150d, -2)]
    public async Task CommitCoordinate_ExactHalf_RoundsTowardNegativeInfinity(
        double planCoordinate,
        int expectedGridCoordinate)
    {
        var actual = SceneViewport.CommitCoordinate(
            planCoordinate,
            gridStepPlanUnits: 100,
            snapStepGridUnits: 1,
            disableSnap: false);

        await Assert.That(actual).IsEqualTo(expectedGridCoordinate);
    }

    [Test]
    [Arguments(1, 4, 0)]
    [Arguments(2, 4, 0)]
    [Arguments(3, 4, 4)]
    [Arguments(-1, 4, 0)]
    [Arguments(-2, 4, -4)]
    [Arguments(-3, 4, -4)]
    public async Task SnapCoordinate_UsesNearestMultipleWithNegativeHalfTie(
        int coordinate,
        int snapStep,
        int expected)
    {
        var actual = SceneViewport.SnapCoordinate(coordinate, snapStep);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task ZoomAt_AnchorPoint_RemainsFixedAndClampsPolicyRange()
    {
        var viewport = new SceneViewport(17, 23, 1, 0.5, 2);
        var anchor = new ScenePoint(320, 180);
        var worldBefore = viewport.ScreenToWorld(anchor);

        var zoomed = viewport.ZoomAt(anchor, 10);
        var worldAfter = zoomed.ScreenToWorld(anchor);

        using (Assert.Multiple())
        {
            await Assert.That(zoomed.Zoom).IsEqualTo(2);
            await Assert.That(worldAfter.X).IsEqualTo(worldBefore.X).Within(0.000_001);
            await Assert.That(worldAfter.Y).IsEqualTo(worldBefore.Y).Within(0.000_001);
        }
    }

    [Test]
    public async Task Transform_NonFinitePoint_IsRejected()
    {
        var viewport = new SceneViewport(0, 0, 1, 0.25, 4);

        var worldToScreen = () => viewport.WorldToScreen(
            new ScenePoint(double.PositiveInfinity, 0));
        var screenToWorld = () => viewport.ScreenToWorld(
            new ScenePoint(0, double.NaN));

        using (Assert.Multiple())
        {
            await Assert.That(worldToScreen).Throws<ArgumentOutOfRangeException>();
            await Assert.That(screenToWorld).Throws<ArgumentOutOfRangeException>();
        }
    }
}
