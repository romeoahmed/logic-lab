using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabBrowserApplication>]
internal sealed class WorkbenchWorkflowTests(LogicLabBrowserApplication application) : PageTest
{
    private const string AndGate = "library:logiclab.core:logic.and";
    private const string NandGate = "library:logiclab.core:logic.nand";

    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;
        return options;
    }

    [Test]
    public async Task ComponentPalette_SearchAndPlace_FiltersAndEnablesCompilation()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();

        await workbench.ComponentSearch.PressSequentiallyAsync("AND");
        await Expect(workbench.PlaceOptions).ToHaveCountAsync(2);
        var andGate = workbench.PlaceOption(AndGate);
        await Expect(andGate).ToBeVisibleAsync();
        await Expect(workbench.PlaceOption(NandGate)).ToBeVisibleAsync();

        await andGate.ClickAsync();
        await Expect(andGate).ToHaveAttributeAsync("aria-pressed", "true");
        await workbench.Canvas.ClickAsync();

        await Expect(andGate).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(workbench.Command("compile")).ToBeEnabledAsync();
    }

    [Test]
    public async Task InverterStarter_CompileSimulateAndStep_ProjectsSignalTransition()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();

        await workbench.Command("author").ClickAsync();
        var compile = workbench.Command("compile");
        await Expect(compile).ToBeEnabledAsync();

        await compile.ClickAsync();
        var session = workbench.Command("session");
        await Expect(session).ToBeEnabledAsync();

        await session.ClickAsync();
        var stimulus = workbench.Command("stimulus");
        await Expect(stimulus).ToBeEnabledAsync();
        await Expect(workbench.Tool("probe")).ToBeEnabledAsync();
        await Expect(workbench.Probes).ToHaveCountAsync(1);
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
        await Expect(workbench.Status("logical-time")).ToHaveTextAsync("0");

        await stimulus.ClickAsync();
        var step = workbench.Command("step");
        await Expect(stimulus).ToBeHiddenAsync();
        await Expect(step).ToBeVisibleAsync();

        await step.ClickAsync();
        await Expect(workbench.Status("logical-time")).ToHaveTextAsync("1");
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("0");
        await Expect(stimulus).ToBeVisibleAsync();
        await Expect(step).ToBeHiddenAsync();
    }
}
