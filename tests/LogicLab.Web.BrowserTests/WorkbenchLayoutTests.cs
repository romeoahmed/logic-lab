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
        await StartSimulationAsync(workbench);
        var status = Page.Locator(".status-strip");
        await AssertSurfaceOrderAsync();
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
    [Arguments(390, 844)]
    [Arguments(694, 838)]
    public async Task ResponsiveWorkbench_NarrowSimulation_KeepsCircuitAndControlsUsable(int width, int height)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync("author-bit-serial", width: width, height: height);
        await StartSimulationAsync(workbench);
        await AssertCircuitAndControlsAsync();

        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await AssertCircuitAndControlsAsync();

        async Task AssertCircuitAndControlsAsync()
        {
            await Expect(workbench.Canvas).ToBeInViewportAsync(new() { Ratio = 1 });
            await Expect(workbench.Step).ToBeInViewportAsync(new() { Ratio = 1 });
            await Expect(workbench.Run).ToBeInViewportAsync(new() { Ratio = 1 });
            await Expect(workbench.LogicalTime).ToBeInViewportAsync(new() { Ratio = 1 });
            await AssertSurfaceOrderAsync();
        }
    }

    [Test]
    public async Task Instruments_DesktopSimulation_ShowsWaveformAndReturnsToCircuit()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync();
        await StartSimulationAsync(workbench);
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

    private async Task StartSimulationAsync(WorkbenchTestPage workbench)
    {
        await workbench.StartSimulation.ClickAsync();
        // A click acknowledges browser input, not the Interactive Server command.
        await Expect(workbench.LogicalTime).ToHaveTextAsync("0");
        await Expect(workbench.Step).ToBeEnabledAsync();
        await Expect(Page.Locator(".command-bar[data-active-command]")).ToHaveCountAsync(0);
        await Page.EvaluateAsync("() => document.fonts.ready.then(() => undefined)");
    }

    private async Task AssertSurfaceOrderAsync()
    {
        // Read all regions in one browser turn. Platform font metrics may change
        // their sizes; the product contract is usable, non-overlapping regions.
        var bounds = await Page.EvaluateAsync<RegionBounds[]>("""
            () => ['.workbench-dock', 'canvas[data-scene-canvas]', '.instrument-bay', '.status-strip']
              .map(selector => {
                const rect = document.querySelector(selector).getBoundingClientRect();
                return { top: rect.top, bottom: rect.bottom, height: rect.height };
              })
            """);
        var dock = bounds[0];
        var canvas = bounds[1];
        var instruments = bounds[2];
        var status = bounds[3];
        await Assert.That(dock.Height).IsGreaterThan(instruments.Height);
        await Assert.That(canvas.Height).IsGreaterThan(0);
        await Assert.That(instruments.Height).IsGreaterThan(0);
        await Assert.That(canvas.Top).IsGreaterThanOrEqualTo(dock.Top);
        await Assert.That(canvas.Bottom).IsLessThanOrEqualTo(dock.Bottom + 1);
        await Assert.That(dock.Bottom).IsLessThanOrEqualTo(instruments.Top + 1);
        await Assert.That(instruments.Bottom).IsLessThanOrEqualTo(status.Top + 1);
    }

    private sealed class RegionBounds
    {
        public double Top { get; set; }

        public double Bottom { get; set; }

        public double Height { get; set; }
    }

}
