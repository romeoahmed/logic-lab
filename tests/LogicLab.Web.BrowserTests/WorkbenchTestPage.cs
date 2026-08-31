using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class WorkbenchTestPage(IPage page, Uri editorUri)
{
    public ILocator Canvas => page.Locator("canvas[data-scene-canvas]");

    public ILocator ComponentSearch => Palette.GetByRole(AriaRole.Searchbox);

    public ILocator PlaceOptions => Palette.Locator("[data-place-option]");

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

    public ILocator WaveformZoomIn => WaveformControl("Time range", "Zoom in");

    public ILocator WaveformSecondaryCursor => WaveformControl("Time cursors", "B");

    public ILocator Command(string command) =>
        page.Locator($"[data-command='{command}']");

    public ILocator PlaceOption(string option) =>
        Palette.Locator($"[data-place-option='{option}']");

    public ILocator Status(string status) =>
        page.Locator($"[data-status='{status}'] dd");

    public ILocator Tool(string tool) =>
        page.Locator($"[data-scene-tool='{tool}']");

    public ILocator WaveformRepresentation(string representation) => WaveformControl(
        "Display detail",
        representation switch
        {
            "transitions" => "Full detail",
            "summary" => "Overview",
            _ => throw new ArgumentOutOfRangeException(nameof(representation)),
        });

    private ILocator Palette => page.GetByTestId("component-palette");

    private ILocator WaveformControl(string group, string label) =>
        Waveform.GetByRole(
                AriaRole.Group,
                new LocatorGetByRoleOptions { Name = group })
            .GetByText(label, new LocatorGetByTextOptions { Exact = true });

    public async Task OpenSandboxAsync(int width = 1_280, int height = 900)
    {
        await page.SetViewportSizeAsync(width, height);
        var response = await page.GotoAsync(editorUri.ToString());

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Ok).IsTrue();

        var createSandbox = Command("create");
        await Expect(createSandbox).ToBeVisibleAsync();
        await createSandbox.ClickAsync();
        await Expect(ComponentSearch).ToBeVisibleAsync();
        await Expect(Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
        await Expect(Canvas).ToBeVisibleAsync();
    }
}
