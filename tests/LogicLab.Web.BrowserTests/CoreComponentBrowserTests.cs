using LogicLab.Domain.Authoring;
using LogicLab.Web.Testing;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabKestrelApplication>(Shared = SharedType.PerClass)]
internal sealed class CoreComponentBrowserTests(LogicLabKestrelApplication application) : PageTest
{
    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;
        return options;
    }

    public static IEnumerable<string> Contracts() => LibrarySnapshot.Core.Contracts.Select(contract => contract.Key.ContractId);

    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task Palette_CoreContract_KeyboardActivationPlacesInspectableSymbolAndSupportsHistory(string contractId)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();
        var option = Page.Locator($"[data-place-option='library:logiclab.core:{contractId}']");
        var group = Page.Locator(".component-groups details").Filter(new() { Has = option });
        if (await group.GetAttributeAsync("open") is null)
        {
            await group.Locator("summary").ClickAsync();
        }
        var name = await option.Locator("strong").InnerTextAsync();
        await option.FocusAsync();
        await option.PressAsync("Enter");
        await Expect(option).ToHaveAttributeAsync("aria-pressed", "true");
        await workbench.Canvas.ClickAsync();
        await Expect(option).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(workbench.Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
        await Expect(Page.Locator("[data-scene-diagnostics]")).ToHaveCountAsync(0);

        // Select the sole placed component by marquee, independent of its symbol geometry.
        await Expect(workbench.Canvas).ToBeEnabledAsync();
        var bounds = (await workbench.Canvas.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(bounds.X + 8, bounds.Y + 8);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(bounds.X + bounds.Width - 8, bounds.Y + bounds.Height - 8, new() { Steps = 4 });
        await Page.Mouse.UpAsync();
        var type = Page.Locator("[data-selection-inspector] dl > div").Filter(new() { HasText = "Type / definition" }).Locator("dd");
        await Expect(type).ToHaveTextAsync(name);
        await Expect(Page.Locator("[data-selection-item]")).ToHaveCountAsync(1);
        await workbench.Undo.ClickAsync();
        await Expect(Page.Locator("[data-selection-item]")).ToHaveCountAsync(0);
        var componentCount = Page.Locator("[data-selection-inspector] dl > div").Filter(new() { HasText = "Components" }).Locator("dd");
        await Expect(componentCount).ToHaveTextAsync("0");
        await workbench.Redo.ClickAsync();
        await Expect(componentCount).ToHaveTextAsync("1");
        await Expect(workbench.Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
    }
}
