using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class WorkbenchTestPage(IPage page, Uri editorUri)
{
    public ILocator Canvas => page.Locator("canvas[data-scene-canvas]");

    public ILocator ComponentSearch => Palette.GetByRole(AriaRole.Searchbox);

    public ILocator PlaceOptions => Palette.Locator("[data-place-option]");

    public ILocator Probes => page.Locator("[data-probe]");

    public ILocator Renderer => page.Locator("[data-scene-renderer]");

    public ILocator Command(string command) =>
        page.Locator($"[data-command='{command}']");

    public ILocator PlaceOption(string option) =>
        Palette.Locator($"[data-place-option='{option}']");

    public ILocator Status(string status) =>
        page.Locator($"[data-status='{status}'] dd");

    public ILocator Tool(string tool) =>
        page.Locator($"[data-scene-tool='{tool}']");

    private ILocator Palette => page.GetByTestId("component-palette");

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
