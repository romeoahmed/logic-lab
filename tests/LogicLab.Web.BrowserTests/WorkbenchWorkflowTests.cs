using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabBrowserApplication>]
internal sealed class WorkbenchWorkflowTests(LogicLabBrowserApplication application) : PageTest
{
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

        await workbench.ComponentSearch.FillAsync("AND");
        await Expect(workbench.PlaceOptions).ToHaveCountAsync(2);
        var andGate = workbench.PlaceOption("AND gate Boolean function");
        await Expect(andGate).ToBeVisibleAsync();
        await Expect(workbench.PlaceOption("NAND gate Boolean function"))
            .ToBeVisibleAsync();

        await andGate.ClickAsync();
        await Expect(andGate).ToHaveAttributeAsync("aria-pressed", "true");
        await workbench.Canvas.ClickAsync();

        await Expect(andGate).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(workbench.Compile).ToBeEnabledAsync();
    }

    [Test]
    public async Task InverterStarter_CompileSimulateAndStep_ProjectsSignalTransition()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();

        await workbench.InverterStarter.ClickAsync();
        var compile = workbench.Compile;
        await Expect(compile).ToBeEnabledAsync();

        await compile.ClickAsync();
        var session = workbench.StartSimulation;
        await Expect(session).ToBeEnabledAsync();

        await session.ClickAsync();
        var stimulus = workbench.SetInputsHigh;
        await Expect(stimulus).ToBeEnabledAsync();
        await Expect(workbench.ProbeTool).ToBeEnabledAsync();
        await Expect(workbench.Probes).ToHaveCountAsync(1);
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
        await Expect(workbench.LogicalTime).ToHaveTextAsync("0");

        await stimulus.ClickAsync();
        var step = workbench.Step;
        await Expect(stimulus).ToBeHiddenAsync();
        await Expect(step).ToBeVisibleAsync();

        await step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("0");
        await Expect(stimulus).ToBeVisibleAsync();
        await Expect(step).ToBeHiddenAsync();
    }

    [Test]
    public async Task LogicAnalyzer_HistorySummaryCursorAndLiveFollow_StayCoherent()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();
        await workbench.InverterStarter.ClickAsync();
        await workbench.Compile.ClickAsync();
        await Expect(workbench.StartSimulation).ToBeEnabledAsync();
        await workbench.StartSimulation.ClickAsync();

        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();

        await workbench.WaveformSummary.ClickAsync();
        await Expect(workbench.WaveformSummary)
            .ToHaveAttributeAsync("pressed", "");

        await workbench.WaveformZoomIn.ClickAsync();
        await Expect(workbench.WaveformLive)
            .Not.ToHaveAttributeAsync("pressed", "");

        await workbench.WaveformCanvas.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 180, Y = 70 },
        });
        var cursorReadout = workbench.Waveform.Locator(".cursor-readout");
        await Expect(cursorReadout
                .GetByText("A", new() { Exact = true }))
            .ToBeVisibleAsync();
        await workbench.WaveformSecondaryCursor.ClickAsync();
        await Expect(cursorReadout
                .GetByText("B", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(cursorReadout
                .GetByText("Δt", new() { Exact = true }))
            .ToBeVisibleAsync();

        await workbench.WaveformLive.ClickAsync();
        await Expect(workbench.WaveformLive)
            .ToHaveAttributeAsync("pressed", "");
        await workbench.WaveformLive.ClickAsync();
        await Expect(workbench.WaveformLive)
            .Not.ToHaveAttributeAsync("pressed", "");

        await workbench.WaveformClose.ClickAsync();
        await Expect(workbench.WaveformOpen).ToBeVisibleAsync();
        await workbench.WaveformOpen.ClickAsync();
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();
    }

    [Test]
    public async Task LogicAnalyzer_NarrowViewport_CoreActionsRemainReachable()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: 390, height: 844);
        await workbench.InverterStarter.ClickAsync();
        await workbench.Compile.ClickAsync();
        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();

        await workbench.WaveformSummary.ScrollIntoViewIfNeededAsync();
        await Expect(workbench.WaveformSummary).ToBeInViewportAsync();
        await Expect(workbench.WaveformZoomIn).ToBeInViewportAsync();
        await Expect(workbench.WaveformLive).ToBeInViewportAsync();
        await Expect(workbench.WaveformClose).ToBeInViewportAsync();
        var waveformBounds = await workbench.WaveformCanvas.BoundingBoxAsync();
        await workbench.WaveformCanvas.ScrollIntoViewIfNeededAsync();
        await Expect(workbench.WaveformCanvas).ToBeInViewportAsync();

        using (Assert.Multiple())
        {
            await Assert.That(waveformBounds).IsNotNull();
            await Assert.That(waveformBounds!.X).IsGreaterThanOrEqualTo(0);
            await Assert.That(waveformBounds.X + waveformBounds.Width)
                .IsLessThanOrEqualTo(390);
        }
    }
}
