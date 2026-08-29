using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabBrowserApplication>]
internal sealed class WorkbenchLayoutTests(LogicLabBrowserApplication application) : PageTest
{
    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;
        return options;
    }

    [Test]
    public async Task ResponsiveWorkbench_NarrowViewport_PrioritizesCanvasAndKeepsLibraryControlsVisible()
    {
        await OpenAsync(390, 844);
        var library = Page.GetByTestId("workbench-library");
        var canvas = Page.GetByTestId("workbench-canvas");

        await Expect(library).ToBeVisibleAsync();
        await Expect(canvas).ToBeVisibleAsync();
        var palette = Page.GetByTestId("component-palette");
        var controls = Page.GetByTestId("component-palette-controls");
        var groups = palette.Locator("details");
        for (var index = 0; index < await groups.CountAsync(); index++)
        {
            var group = groups.Nth(index);
            if (await group.GetAttributeAsync("open") is null)
            {
                await group.Locator("summary").ClickAsync();
            }
        }

        var scroll = await palette.EvaluateAsync<PaletteScrollState>(
            """
            element => {
              element.scrollTop = element.scrollHeight;
              return {
                clientHeight: element.clientHeight,
                scrollHeight: element.scrollHeight,
                scrollTop: element.scrollTop,
              };
            }
            """);
        await Expect(controls).ToBeVisibleAsync();
        var libraryBounds = await library.BoundingBoxAsync();
        var canvasBounds = await canvas.BoundingBoxAsync();
        var paletteBounds = await palette.BoundingBoxAsync();
        var controlsBounds = await controls.BoundingBoxAsync();

        using (Assert.Multiple())
        {
            await Assert.That(libraryBounds).IsNotNull();
            await Assert.That(canvasBounds).IsNotNull();
            await Assert.That(paletteBounds).IsNotNull();
            await Assert.That(controlsBounds).IsNotNull();
            await Assert.That(canvasBounds!.Y).IsLessThan(libraryBounds!.Y);
            await Assert.That(scroll.ScrollHeight).IsGreaterThan(scroll.ClientHeight);
            await Assert.That(scroll.ScrollTop).IsGreaterThan(0);
            await Assert.That(controlsBounds!.Y)
                .IsEqualTo(paletteBounds!.Y).Within(1.5F);
        }
    }

    [Test]
    [Arguments(768, 1024)]
    [Arguments(1024, 768)]
    public async Task ResponsiveWorkbench_MediumViewport_KeepsCanvasInsideViewport(
        int width,
        int height)
    {
        await OpenAsync(width, height);
        var library = Page.GetByTestId("workbench-library");
        var canvas = Page.GetByTestId("workbench-canvas");

        await Expect(library).ToBeVisibleAsync();
        await Expect(canvas).ToBeVisibleAsync();
        var libraryBounds = await library.BoundingBoxAsync();
        var canvasBounds = await canvas.BoundingBoxAsync();
        var documentWidth = await Page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth");

        using (Assert.Multiple())
        {
            await Assert.That(libraryBounds).IsNotNull();
            await Assert.That(canvasBounds).IsNotNull();
            await Assert.That(libraryBounds!.Y).IsEqualTo(canvasBounds!.Y).Within(1.5F);
            await Assert.That(libraryBounds.X).IsLessThan(canvasBounds.X);
            await Assert.That(canvasBounds.X).IsGreaterThanOrEqualTo(0);
            await Assert.That(canvasBounds.X + canvasBounds.Width).IsLessThanOrEqualTo(width);
            await Assert.That(documentWidth).IsLessThanOrEqualTo(width);
        }
    }

    [Test]
    public async Task ProjectOptions_FluentPopover_OpensAndLightDismisses()
    {
        await OpenAsync(1024, 768);
        var trigger = Page.GetByTestId("project-options-trigger");
        var panel = Page.GetByTestId("project-options-panel");

        await Expect(panel).ToBeHiddenAsync();
        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
    }

    private async Task OpenAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        var response = await Page.GotoAsync(application.EditorUri.ToString());

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Ok).IsTrue();

        var createSandbox = Page.Locator("[data-command='create']");
        await Expect(createSandbox).ToBeVisibleAsync();
        await createSandbox.ClickAsync();
        await Expect(Page.Locator("[data-component-search]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-scene-renderer='ready']")).ToBeVisibleAsync();
    }

    private sealed class PaletteScrollState
    {
        public int ClientHeight { get; set; }

        public int ScrollHeight { get; set; }

        public int ScrollTop { get; set; }
    }
}
