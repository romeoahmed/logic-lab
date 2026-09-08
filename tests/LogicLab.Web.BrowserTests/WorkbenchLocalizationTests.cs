using LogicLab.Web.Testing;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabKestrelApplication>(Shared = SharedType.PerClass)]
internal sealed class WorkbenchLocalizationTests(LogicLabKestrelApplication application) : PageTest
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
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync("author-bit-serial");
        await workbench.StartSimulation.ClickAsync();
        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        var workspaceUrl = Page.Url;
        var logicalTime = 1;

        foreach (var culture in new[] { "en-US", "zh-CN" })
        {
            var form = Page.Locator("[data-culture-form]");
            await form.Locator("select").SelectOptionAsync(culture);
            await form.Locator("fluent-button").ClickAsync();
            await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", culture);
            // The new language proves document commit, not completed loading or
            // Interactive Server attachment. Wait for those phases separately.
            await Page.WaitForLoadStateAsync(LoadState.Load);
            await Expect(workbench.Step).ToBeEnabledAsync();
            await Expect(workbench.Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
            await Expect(workbench.LogicalTime).ToHaveTextAsync(TimeText());
            await Expect(workbench.Canvas).ToHaveAttributeAsync("lang", culture);
            await Assert.That(Page.Url).IsEqualTo(workspaceUrl);

            await workbench.Step.ClickAsync();
            logicalTime++;
            await Expect(workbench.LogicalTime).ToHaveTextAsync(TimeText());
        }

        string TimeText() => logicalTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
