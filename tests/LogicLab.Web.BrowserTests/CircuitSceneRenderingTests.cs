using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneRenderingTests : PageTest
{
    [Test]
    public async Task PublishedComponents_CanvasPixelsPreserveSeparationAndScale()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();

        var clusters = await scene.CanvasInkClustersAsync();

        await Assert.That(clusters).Count().IsEqualTo(2);
        var first = clusters[0];
        var second = clusters[1];
        using (Assert.Multiple())
        {
            await Assert.That(first.PixelCount).IsGreaterThan(0);
            await Assert.That(second.PixelCount).IsGreaterThan(0);
            await Assert.That(first.Right).IsLessThan(second.Left);
            await Assert.That(Math.Abs(Width(first) - Width(second))).IsLessThanOrEqualTo(2);
            await Assert.That(Math.Abs(Height(first) - Height(second))).IsLessThanOrEqualTo(2);
            await Assert.That(Math.Abs(first.Top - second.Top)).IsLessThanOrEqualTo(2);
        }
    }

    [Test]
    public async Task PublishedScene_CanvasRendersVisibleSnapGrid()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();

        var contrast = await scene.MaximumCanvasColumnContrastAsync(
            SceneTestSnapshot.Bounds.Left);

        await Assert.That(contrast).IsGreaterThan(0.5);
    }

    private static int Width(CanvasInkCluster cluster) => cluster.Right - cluster.Left + 1;

    private static int Height(CanvasInkCluster cluster) => cluster.Bottom - cluster.Top + 1;
}
