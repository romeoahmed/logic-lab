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
    public async Task ResponsiveWorkbench_NarrowViewport_PlacesLibraryBeforeCanvas()
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
            await Assert.That(libraryBounds!.Y).IsLessThan(canvasBounds!.Y);
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
        var workspace = Page.GetByTestId("workbench-canvas");

        await Expect(workspace).ToBeVisibleAsync();
        var bounds = await workspace.BoundingBoxAsync();
        var documentWidth = await Page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth");

        using (Assert.Multiple())
        {
            await Assert.That(bounds).IsNotNull();
            await Assert.That(bounds!.X).IsGreaterThanOrEqualTo(0);
            await Assert.That(bounds.X + bounds.Width).IsLessThanOrEqualTo(width);
            await Assert.That(documentWidth).IsLessThanOrEqualTo(width);
        }
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
