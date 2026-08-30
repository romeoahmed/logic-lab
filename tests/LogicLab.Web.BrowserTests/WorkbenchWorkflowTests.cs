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

    [Test]
    public async Task LogicAnalyzer_HistorySummaryCursorAndLiveFollow_StayCoherent()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();
        await workbench.Command("author").ClickAsync();
        await workbench.Command("compile").ClickAsync();
        await Expect(workbench.Command("session")).ToBeEnabledAsync();
        await workbench.Command("session").ClickAsync();

        await Expect(workbench.Waveform)
            .ToHaveAttributeAsync("data-waveform-renderer", "ready");
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();
        await Expect(workbench.WaveformLive).ToHaveAttributeAsync("aria-pressed", "true");

        await workbench.WaveformRepresentation("summary").ClickAsync();
        await Expect(workbench.Waveform)
            .ToHaveAttributeAsync("data-waveform-resolution", "summary");
        await Expect(Page.Locator(".summary-resolution")).ToBeVisibleAsync();

        await Page.GetByTitle("Zoom in").Last.ClickAsync();
        await Expect(workbench.WaveformLive).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(Page.Locator("[data-waveform-history]")).ToBeVisibleAsync();

        await workbench.WaveformCanvas.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 180, Y = 70 },
        });
        await Expect(Page.Locator("[data-waveform-primary-cursor]"))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Page.Locator("[data-waveform-cursor='primary']"))
            .ToBeVisibleAsync();
        await Page.Locator("[data-waveform-secondary-cursor]").ClickAsync();
        await Expect(Page.Locator("[data-waveform-cursor='secondary']"))
            .ToBeVisibleAsync();
        await Expect(Page.Locator("[data-waveform-cursor-delta]"))
            .ToBeVisibleAsync();

        await workbench.WaveformLive.ClickAsync();
        await Expect(workbench.WaveformLive).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Page.Locator("[data-waveform-history]")).ToBeHiddenAsync();
        var radix = workbench.Probes.Locator("[data-probe-radix]");
        await radix.ClickAsync();
        var hexadecimal = radix.Locator("fluent-option[value='hex']");
        await hexadecimal.ClickAsync();
        await Expect(hexadecimal).ToHaveAttributeAsync("current-selected", string.Empty);
        var firstProbe = workbench.Probes.First;
        var probeLabel = await firstProbe.Locator("[data-probe-label]").InnerTextAsync();
        await firstProbe.Locator("[data-probe-reveal]").ClickAsync();
        await Expect(Page.Locator(".status-message")).ToContainTextAsync(probeLabel);

        await workbench.WaveformClose.ClickAsync();
        await Expect(workbench.WaveformOpen).ToBeVisibleAsync();
        await workbench.WaveformOpen.ClickAsync();
        await Expect(workbench.Waveform)
            .ToHaveAttributeAsync("data-waveform-renderer", "ready");
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();
    }

    [Test]
    public async Task LogicAnalyzer_NarrowViewport_CoreActionsRemainReachable()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: 390, height: 844);
        await workbench.Command("author").ClickAsync();
        await workbench.Command("compile").ClickAsync();
        await workbench.Command("session").ClickAsync();
        await Expect(workbench.Waveform)
            .ToHaveAttributeAsync("data-waveform-renderer", "ready");
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();

        var summary = workbench.WaveformRepresentation("summary");
        await Expect(summary).ToBeVisibleAsync();
        await summary.FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(workbench.Waveform)
            .ToHaveAttributeAsync("data-waveform-resolution", "summary");

        await Page.GetByTitle("Zoom in").Last.ClickAsync();
        await Expect(workbench.WaveformLive).ToHaveAttributeAsync("aria-pressed", "false");
        await workbench.WaveformLive.ClickAsync();
        await Expect(workbench.WaveformLive).ToHaveAttributeAsync("aria-pressed", "true");

        await Expect(workbench.WaveformClose).ToBeVisibleAsync();
        await workbench.WaveformClose.ClickAsync();
        await Expect(workbench.WaveformOpen).ToBeVisibleAsync();
        await workbench.WaveformOpen.ClickAsync();
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();
    }
}
