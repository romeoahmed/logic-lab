using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabBrowserApplication>]
internal sealed class WorkbenchLocalizationTests(LogicLabBrowserApplication application) : PageTest
{
    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;
        options.Locale = "zh-CN";
        return options;
    }

    [Test]
    public async Task ChineseChrome_BeforeCanvasMount_UsesPackagedFontForNativeAndFluentText()
    {
        await Page.GotoAsync(new Uri(application.EditorUri, "/").ToString());
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "zh-CN");
        var button = Page.Locator("fluent-anchor-button").First;
        await Expect(button).ToBeVisibleAsync();
        var fonts = await Page.EvaluateAsync<string[]>(
            """
            async () => {
              await document.fonts.ready;
              const faces = await document.fonts.load('16px "Logic Lab UI CJK"', '工作台');
              return [
                String(faces.length),
                getComputedStyle(document.querySelector('h1')).fontFamily,
                getComputedStyle(document.querySelector('fluent-anchor-button')).fontFamily,
              ];
            }
            """);

        using (Assert.Multiple())
        {
            await Assert.That(fonts[0]).IsEqualTo("1");
            await Assert.That(fonts[1]).Contains("Logic Lab UI CJK");
            await Assert.That(fonts[2]).IsEqualTo(fonts[1]);
        }
    }

    [Test]
    public async Task CultureForm_ExampleWorkspace_RoundTripRetainsCircuitAndSession()
    {
        await Page.GotoAsync(application.EditorUri.ToString());
        await Page.Locator("[data-command='author-bit-serial']").ClickAsync();
        var renderer = Page.Locator("[data-scene-renderer]");
        await Expect(renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
        await Page.Locator("[data-command='session']").ClickAsync();
        var step = Page.Locator("[data-command='step']");
        await Expect(step).ToBeEnabledAsync();
        await step.ClickAsync();
        await Expect(Page.Locator("[data-status='logical-time'] dd")).ToHaveTextAsync("1");
        var workspaceUrl = Page.Url;
        var logicalTime = await Page.Locator("[data-status='logical-time'] dd").InnerTextAsync();

        foreach (var culture in new[] { "en-US", "zh-CN" })
        {
            var form = Page.Locator("[data-culture-form]");
            await form.Locator("select").SelectOptionAsync(culture);
            await form.Locator("fluent-button").ClickAsync();
            await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", culture);
            await Expect(renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
            await Expect(step).ToBeEnabledAsync();
            await Expect(Page.Locator("[data-status='logical-time'] dd")).ToHaveTextAsync(logicalTime);
            await Expect(Page.Locator("canvas[data-scene-canvas]")).ToHaveAttributeAsync("lang", culture);
            await Assert.That(Page.Url).IsEqualTo(workspaceUrl);
        }
    }
}
