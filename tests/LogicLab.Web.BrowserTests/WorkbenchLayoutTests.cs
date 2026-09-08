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
    [Arguments(390, 844)]
    [Arguments(694, 838)]
    [Arguments(768, 1024)]
    [Arguments(1024, 768)]
    [Arguments(1280, 900)]
    public async Task ResponsiveWorkbench_ActiveProject_KeepsCanvasInstrumentsAndStatusInViewport(
        int width, int height)
    {
        await OpenAsync(width, height);
        var bounds = await Page.EvaluateAsync<double[]>(
            """
            () => [document.documentElement.scrollWidth, document.documentElement.scrollHeight]
            """);
        await Assert.That(bounds[0]).IsLessThanOrEqualTo(width);
        await Assert.That(bounds[1]).IsLessThanOrEqualTo(height);
        await Expect(Page.Locator("canvas[data-scene-canvas]")).ToBeInViewportAsync();
        await Expect(Page.Locator(".instrument-bay")).ToBeInViewportAsync();
        await Expect(Page.Locator(".status-strip")).ToBeInViewportAsync();
    }

    [Test]
    [Arguments(844, 390, 100)]
    [Arguments(640, 360, 100)]
    [Arguments(844, 390, 200)]
    public async Task ResponsiveWorkbench_ShortSimulation_ScrollsWithoutClippingInstrumentsOrStatus(
        int width, int height, int textSizePercent)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync(width: width, height: height);
        await Page.EvaluateAsync("size => document.documentElement.style.fontSize = `${size}%`", textSizePercent);
        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.Step).ToBeEnabledAsync();
        var instruments = Page.Locator(".instrument-bay");
        var status = Page.Locator(".status-strip");
        var canvasBounds = (await workbench.Canvas.BoundingBoxAsync())!;
        var instrumentBounds = (await instruments.BoundingBoxAsync())!;
        var statusBounds = (await status.BoundingBoxAsync())!;
        await Assert.That(canvasBounds.Height).IsGreaterThan(100);
        await Assert.That(instrumentBounds.Height).IsGreaterThan(50);
        await Assert.That(canvasBounds.Y + canvasBounds.Height).IsLessThanOrEqualTo(instrumentBounds.Y + 1);
        await Assert.That(instrumentBounds.Y + instrumentBounds.Height).IsLessThanOrEqualTo(statusBounds.Y + 1);
        await status.ScrollIntoViewIfNeededAsync();
        await Expect(workbench.LogicalTime).ToBeInViewportAsync();
        var toggle = Page.Locator("[data-instruments-toggle]");
        await toggle.ClickAsync();
        await Expect(workbench.Canvas).ToBeHiddenAsync();
        await workbench.WaveformCanvas.ScrollIntoViewIfNeededAsync();
        await Expect(workbench.WaveformCanvas).ToBeInViewportAsync();
        await toggle.ClickAsync();
        await workbench.Canvas.ScrollIntoViewIfNeededAsync();
        await Expect(workbench.Canvas).ToBeInViewportAsync();
        var overflow = await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('body, body > *, .site-header-inner, .workbench-shell, .workbench-deck')]
              .filter(element => element.getBoundingClientRect().right > innerWidth + 1)
              .map(element => `${element.tagName}.${element.className}: ${element.getBoundingClientRect().right}`)
            """);
        await Assert.That(overflow).IsEmpty();
        await Assert.That(await Page.EvaluateAsync<double>(
            "document.documentElement.scrollWidth - window.innerWidth")).IsLessThanOrEqualTo(0);
    }

    [Test]
    public async Task ResponsiveWorkbench_NarrowPanels_ToggleAndEscapeRestoreFocus()
    {
        await OpenAsync(390, 844);
        var library = Page.GetByTestId("workbench-library");
        var inspector = Page.GetByTestId("workbench-inspector");
        var libraryToggle = Page.Locator("[data-dock-toggle='library']");
        var inspectorToggle = Page.Locator("[data-dock-toggle='inspector']");
        await Expect(library).ToBeHiddenAsync();
        await Expect(inspector).ToBeHiddenAsync();
        await libraryToggle.ClickAsync();
        await Expect(library).ToBeInViewportAsync();
        await library.GetByRole(AriaRole.Searchbox).FillAsync("clock");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(library).ToBeHiddenAsync();
        await Expect(libraryToggle).ToBeFocusedAsync();
        await inspectorToggle.ClickAsync();
        await Expect(inspector).ToBeInViewportAsync();
        await libraryToggle.ClickAsync();
        await Expect(inspector).ToBeHiddenAsync();
        await Expect(library).ToBeVisibleAsync();
        await Page.SetViewportSizeAsync(1280, 900);
        await Expect(library).ToBeVisibleAsync();
        await Expect(inspector).ToBeVisibleAsync();
        await Expect(libraryToggle).ToBeHiddenAsync();
    }

    [Test]
    public async Task ResponsiveWorkbench_NarrowSimulation_KeepsUsefulCanvasHeight()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync(width: 390, height: 844);
        await workbench.StartSimulation.ClickAsync();
        var canvas = await workbench.Canvas.BoundingBoxAsync();
        await Assert.That(canvas).IsNotNull();
        await Assert.That(canvas!.Height).IsGreaterThanOrEqualTo(180);
        await Expect(workbench.Step).ToBeInViewportAsync();
        await Expect(workbench.Run).ToBeInViewportAsync();
        await Expect(workbench.LogicalTime).ToBeInViewportAsync();
    }

    [Test]
    public async Task Instruments_DesktopSimulation_ShowsWaveformAndReturnsToCircuit()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync();
        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.WaveformCanvas).ToBeInViewportAsync();
        var toggle = Page.Locator("[data-instruments-toggle]");
        await toggle.ClickAsync();
        await Expect(workbench.Canvas).ToBeHiddenAsync();
        await Expect(workbench.LogicalTime).ToBeInViewportAsync();
        await toggle.ClickAsync();
        await Expect(workbench.Canvas).ToBeInViewportAsync();
        await Expect(workbench.WaveformCanvas).ToBeInViewportAsync();
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
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width, height);
    }

}
