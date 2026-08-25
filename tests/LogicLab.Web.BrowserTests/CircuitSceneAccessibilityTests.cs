using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneAccessibilityTests : PageTest
{
    [Test]
    public async Task SemanticAction_EnterAndSpace_UseNativeButtonActivation()
    {
        var scene = await ReadySceneAsync(Page);
        var action = Page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "Nudge Component A", Exact = true });
        await Expect(action).ToBeVisibleAsync();
        await action.FocusAsync();

        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.PressAsync(" ");

        await Expect(scene.EventLog).ToHaveAttributeAsync("data-semantic-action", "nudge");
        await Expect(scene.EventLog).ToHaveAttributeAsync("data-semantic-actions", "2");
    }

    [Test]
    public async Task SceneCanvas_ProductionStylesDisableNativeTouchGestures()
    {
        var scene = await ReadySceneAsync(Page);

        var touchAction = await scene.Canvas.EvaluateAsync<string>(
            "canvas => getComputedStyle(canvas).touchAction");

        await Assert.That(touchAction).IsEqualTo("none");
    }

    [Test]
    public async Task ReducedMotion_SemanticFallbackRemainsOperable()
    {
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions
        {
            ReducedMotion = ReducedMotion.Reduce,
        });
        var scene = await ReadySceneAsync(Page);
        var component = scene.SemanticSource("Component A");

        await Expect(component).ToBeVisibleAsync();
        await Expect(component).ToBeEnabledAsync();
        await component.ClickAsync();

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-semantic-source",
            LogicLab.Web.BrowserTests.SceneTestSnapshot.SourceA.Key);
    }

    [Test]
    public async Task HighDensityForcedColors_CanvasAndFallbackRemainUsable()
    {
        await using var context = await NewContext(new BrowserNewContextOptions
        {
            DeviceScaleFactor = 2,
            ForcedColors = ForcedColors.Active,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 1_280, Height = 800 },
        });
        var densityPage = await context.NewPageAsync();
        var scene = await ReadySceneAsync(densityPage);

        await Expect(scene.Canvas).ToHaveAttributeAsync("width", "1200");
        await Expect(scene.Canvas).ToHaveAttributeAsync("height", "800");
        await Expect(scene.SemanticSource("Component A")).ToBeVisibleAsync();
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
}
