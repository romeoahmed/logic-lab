using Microsoft.Playwright;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneSurfaceTests : PageTest
{
    [Test]
    public async Task ZeroSize_ReplacementSuspendsPainting_AndShowingResumesIt()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();
        await WaitForFramesAsync();
        await scene.Canvas.EvaluateAsync("""
            canvas => {
              const context = canvas.getContext('2d');
              const fillRect = context.fillRect.bind(context);
              window.scenePaintCount = 0;
              context.fillRect = (...args) => {
                window.scenePaintCount++;
                return fillRect(...args);
              };
              document.querySelector('[data-testid="scene-host"]').style.display = 'none';
            }
            """);
        await WaitForFramesAsync();

        await scene.PublishAsync(sceneVersion: 2);
        await WaitForFramesAsync();
        var hiddenPaints = await Page.EvaluateAsync<int>("() => window.scenePaintCount");
        await Page.EvaluateAsync("""
            () => document.querySelector('[data-testid="scene-host"]').style.display = ''
            """);
        await WaitForFramesAsync();

        using (Assert.Multiple())
        {
            await Assert.That(hiddenPaints).IsEqualTo(0);
            await Assert.That(await Page.EvaluateAsync<int>("() => window.scenePaintCount"))
                .IsGreaterThan(0);
        }
    }

    [Test]
    public async Task SceneCanvas_ProductionStylesReservePointerGesturesForTheEditor()
    {
        var scene = await ReadySceneAsync(Page);

        var touchAction = await scene.Canvas.EvaluateAsync<string>(
            "canvas => getComputedStyle(canvas).touchAction");

        await Assert.That(touchAction).IsEqualTo("none");
    }

    [Test]
    public async Task HighDensityDisplay_CanvasBitmapMatchesItsCssExtent()
    {
        await using var context = await NewContext(new BrowserNewContextOptions
        {
            DeviceScaleFactor = 2,
            ViewportSize = new ViewportSize { Width = 1_280, Height = 800 },
        });
        var densityPage = await context.NewPageAsync();
        var scene = await ReadySceneAsync(densityPage);

        await Microsoft.Playwright.Assertions.Expect(scene.Canvas)
            .ToHaveAttributeAsync("width", "1200");
        await Microsoft.Playwright.Assertions.Expect(scene.Canvas)
            .ToHaveAttributeAsync("height", "800");
        var box = await scene.Canvas.BoundingBoxAsync();

        using (Assert.Multiple())
        {
            await Assert.That(box).IsNotNull();
            await Assert.That(box!.Width).IsEqualTo(600).Within(0.01F);
            await Assert.That(box.Height).IsEqualTo(400).Within(0.01F);
        }
    }

    private static async Task<CircuitSceneTestPage> ReadySceneAsync(IPage page)
    {
        var scene = new CircuitSceneTestPage(page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();
        return scene;
    }
    private async Task WaitForFramesAsync() => await Page.EvaluateAsync("""
        () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
        """);
}
