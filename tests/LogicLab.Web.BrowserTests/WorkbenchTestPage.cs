using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class WorkbenchTestPage(IPage page, Uri editorUri)
{
    public ILocator Canvas => page.Locator("canvas[data-scene-canvas]");

    public ILocator ComponentSearch => Palette.GetByRole(AriaRole.Searchbox);

    public ILocator PlaceOptions => Palette.GetByRole(AriaRole.Button);

    public ILocator Compile => Command("compile");

    public ILocator Undo => Command("undo");

    public ILocator Redo => Command("redo");

    public ILocator LogicalTime => page.Locator("[data-status='logical-time'] dd");

    public ILocator ProbeTool => page.Locator("[data-scene-tool='probe']");

    public ILocator Probes => Waveform.GetByRole(AriaRole.Listitem);

    public ILocator Renderer => page.Locator("[data-scene-renderer]");

    public ILocator Waveform => page.GetByRole(
        AriaRole.Region,
        new PageGetByRoleOptions { Name = "Logic Analyzer" });

    public ILocator WaveformCanvas => Waveform.GetByLabel("Waveform chart");

    public ILocator WaveformLive => WaveformControl("Time range", "Follow live");

    public ILocator WaveformClose => Waveform.GetByText(
        "Hide",
        new LocatorGetByTextOptions { Exact = true });

    public ILocator WaveformOpen => Waveform.GetByText(
        "Show waveform",
        new LocatorGetByTextOptions { Exact = true });

    public ILocator WaveformSummary => WaveformControl("Display detail", "Overview");

    public ILocator WaveformZoomIn => WaveformControl("Time range", "Zoom in");

    public ILocator WaveformSecondaryCursor => WaveformControl("Time cursors", "B");

    public ILocator ApplyInputs => Command("stimulus");

    public ILocator StartSimulation => Command("session");

    public ILocator RestartSimulation => Command("restart");

    public ILocator CloseSimulation => Command("close-session");

    public ILocator Step => Command("step");

    public ILocator Run => Command("run");

    public ILocator Pause => Command("pause");

    public ILocator SimulationStatus => page.Locator("[data-status='simulation'] dd");

    public ILocator PlaceOption(string accessibleName) =>
        Palette.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions { Name = accessibleName, Exact = true });

    private ILocator Palette => page.GetByRole(
        AriaRole.Complementary,
        new PageGetByRoleOptions { Name = "Components", Exact = true });

    public ILocator Command(string command) =>
        // Playwright does not treat a custom element's disabled attribute as
        // native actionability. Wait for Fluent's command state explicitly.
        page.Locator($"[data-command='{command}']:not([disabled])");

    private ILocator WaveformControl(string group, string label) =>
        Waveform.GetByRole(
                AriaRole.Group,
                new LocatorGetByRoleOptions { Name = group })
            .GetByText(label, new LocatorGetByTextOptions { Exact = true });

    public async Task OpenLibraryAsync() => await OpenPanelAsync("library");

    public async Task OpenInspectorAsync() => await OpenPanelAsync("inspector");

    private async Task OpenPanelAsync(string panel)
    {
        var toggle = page.Locator($"[data-dock-toggle='{panel}']");
        if (await toggle.IsVisibleAsync() && await toggle.GetAttributeAsync("aria-expanded") != "true")
        {
            await toggle.ClickAsync();
        }
    }

    public async Task SetAllInputsHighAsync()
    {
        await OpenInspectorAsync();
        var inputs = page.Locator("[data-input-stimulus]").GetByRole(AriaRole.Textbox);
        await Expect(inputs.First).ToBeVisibleAsync();
        var inputCount = await inputs.CountAsync();
        for (var index = 0; index < inputCount; index++)
        {
            var input = inputs.Nth(index);
            var width = int.Parse((await input.GetAttributeAsync("maxlength"))!, System.Globalization.CultureInfo.InvariantCulture);
            await input.FillAsync(new string('1', width));
        }
    }

    public async Task ApplyInputsAsync()
    {
        var nextTime = checked(ulong.Parse(
            (await LogicalTime.TextContentAsync())!,
            System.Globalization.CultureInfo.InvariantCulture) + 1);
        await ApplyInputs.ClickAsync();
        // The browser click returns before the server acknowledges the batch.
        await Expect(page.Locator(".status-message")).ToHaveTextAsync(
            $"Input values scheduled for logical time {nextTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
    }

    public async Task OpenExampleAsync(string command = "author", int width = 1_280, int height = 900)
    {
        await page.SetViewportSizeAsync(width, height);
        var response = await page.GotoAsync(editorUri.ToString());
        await Assert.That(response!.Status).IsEqualTo(200);
        await Command(command).ClickAsync();
        await Expect(StartSimulation).ToBeEnabledAsync();
        await WaitForCanvasAsync();
    }

    public async Task OpenSandboxAsync(int width = 1_280, int height = 900)
    {
        await page.SetViewportSizeAsync(width, height);
        var response = await page.GotoAsync(editorUri.ToString());

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Status).IsEqualTo(200);

        var createSandbox = Command("create");
        await Expect(createSandbox).ToBeVisibleAsync();
        await createSandbox.ClickAsync();
        await WaitForCanvasAsync();
    }

    private async Task WaitForCanvasAsync()
    {
        // Initialization uses Playwright's operation budget; a failed renderer still fails the assertion.
        await page.Locator("[data-scene-renderer]:not([data-scene-renderer='starting'])").WaitForAsync();
        await Expect(Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
        await Expect(Canvas).ToBeVisibleAsync();
    }

}
