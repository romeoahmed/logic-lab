using LogicLab.Application.Examples;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.ProjectFormat;
using LogicLab.Web.Testing;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabKestrelApplication>(Shared = SharedType.PerClass)]
internal sealed class WorkbenchWorkflowTests(LogicLabKestrelApplication application) : PageTest
{
    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;
        return options;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WorkbenchNavigation_OpenExample_ReturnsToChooserAndPreservesPriorWorkspace(bool reload)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync("author-bit-serial");
        await workbench.StartSimulation.ClickAsync();
        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        var workspaceUrl = Page.Url;
        if (reload)
        {
            await Page.ReloadAsync();
            await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        }

        await Page.Locator(".primary-navigation a[href='/editor']").ClickAsync();

        await Expect(Page.Locator("[data-command='author-bit-serial']")).ToBeVisibleAsync();
        await Expect(workbench.Canvas).ToBeHiddenAsync();
        await Expect(workbench.LogicalTime).ToBeHiddenAsync();
        await Page.GoBackAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await Assert.That(Page.Url).IsEqualTo(workspaceUrl);
    }

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task ProjectImport_NativePicker_OpensCompiledWorkspace(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: width);
        await Page.GetByTestId("project-options-trigger").ClickAsync();
        var chooser = await Page.RunAndWaitForFileChooserAsync(() =>
            Page.Locator("[data-command='import']").ClickAsync());
        await using var package = typeof(ExampleProject).Assembly.GetManifestResourceStream(
            "LogicLab.Application.Examples.Assets.Inverter.logiclab")!;
        using var buffer = new MemoryStream();
        await package.CopyToAsync(buffer);
        await chooser.SetFilesAsync(new FilePayload
        {
            Name = "inverter.logiclab",
            MimeType = "application/vnd.logiclab+zip",
            Buffer = buffer.ToArray(),
        });

        await Expect(workbench.StartSimulation).ToBeVisibleAsync();
        await Expect(workbench.Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task ProjectImport_IncompleteCircuit_OpensDiagnosticsAndAllowsEditing(int width)
    {
        var revision = await StarterCircuitFixture.LoadAsync(ExampleProject.Inverter);
        var definition = revision.Document.EntryCircuitDefinition;
        var incomplete = ((EditCommitted)ProjectEditor.Apply(revision,
            new PlaceComponentInstanceIntent(
                definition.Id,
                new ComponentContractKey(LibrarySnapshot.Core.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0))))).Revision;
        using var buffer = new MemoryStream();
        var write = await ProjectPackage.WriteAsync(new ProjectPackageWriteRequest(
            incomplete, buffer, PackagePolicy.Default), CancellationToken.None);
        await Assert.That(write).IsTypeOf<PackageWriteSucceeded>();

        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: width);
        await Page.GetByTestId("project-options-trigger").ClickAsync();
        var chooser = await Page.RunAndWaitForFileChooserAsync(() =>
            Page.Locator("[data-command='import']").ClickAsync());
        await chooser.SetFilesAsync(new FilePayload
        {
            Name = "unfinished.logiclab",
            MimeType = "application/vnd.logiclab+zip",
            Buffer = buffer.ToArray(),
        });

        await Expect(Page.Locator("[data-diagnostic-code='compiler_required_terminal_unconnected']"))
            .ToBeVisibleAsync();
        await Expect(workbench.Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
        await Expect(Page.Locator("[data-command='session']")).ToHaveAttributeAsync("disabled", "");
        await Expect(workbench.Compile).ToBeVisibleAsync();
        await workbench.OpenLibraryAsync();
        await Page.Locator("[data-place-option$=':source.input']").ClickAsync();
        await workbench.Canvas.ClickAsync();
        await Expect(workbench.Undo).ToBeVisibleAsync();
        await workbench.Undo.ClickAsync();
        await Expect(workbench.Redo).ToBeVisibleAsync();
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [Test]
    public async Task WorkbenchRecovery_MissingWorkspace_StartsFreshSandbox()
    {
        await Page.GotoAsync(new Uri(application.EditorUri, "/editor/missing-workspace").ToString());
        await Expect(Page.Locator("[data-workspace-attachment-error]")).ToBeVisibleAsync();
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

        await Page.Locator("[data-recovery='sandbox']").ClickAsync();

        await Expect(Page.Locator("[data-command='author-bit-serial']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-workspace-attachment-error]")).ToBeHiddenAsync();
        await Page.Locator("[data-command='create']:not([disabled])").ClickAsync();
        await Expect(Page.Locator("[data-scene-renderer]")).ToHaveAttributeAsync("data-scene-renderer", "ready");
    }

    [Test]
    public async Task CarryLookahead_IndependentInputs_UpdatesSumAndCarry()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync("author-carry-lookahead");
        await workbench.StartSimulation.ClickAsync();
        await Input("A").FillAsync("1111");
        await Input("B").FillAsync("0001");
        await Input("Carry in").FillAsync("1");

        await workbench.ApplyInputsAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("0");
        await workbench.Step.ClickAsync();

        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync(["0001", "1"]);
    }

    [Test]
    public async Task BitSerial_LoadAndRelease_ProducesSumAfterFourShiftEdges()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync("author-bit-serial");
        await workbench.StartSimulation.ClickAsync();
        await Input("A").FillAsync("0011");
        await Input("B").FillAsync("0101");
        await Input("Load operands").FillAsync("1");
        await workbench.ApplyInputsAsync();
        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await Input("Load operands").FillAsync("0");
        await workbench.ApplyInputsAsync();

        for (var time = 2; time <= 9; time++)
        {
            await workbench.Step.ClickAsync();
            await Expect(workbench.LogicalTime).ToHaveTextAsync(time.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync(["1000", "0"]);
        await workbench.RestartSimulation.ClickAsync();
        await Expect(Input("A")).ToHaveValueAsync("1011");
        await Expect(Input("B")).ToHaveValueAsync("0110");
        await Expect(Input("Load operands")).ToHaveValueAsync("0");
    }

    private ILocator Input(string name) => Page.GetByRole(
        AriaRole.Textbox, new PageGetByRoleOptions { Name = name, Exact = true });

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task ProbeIdentity_ReorderAndReveal_PreservesSharedInspectorCue(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync("author-carry-lookahead", width: width);
        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.Probes).ToHaveCountAsync(2);
        await workbench.OpenLibraryAsync();
        await Page.Locator("[data-instruments-toggle]").ClickAsync();
        var original = workbench.Probes.First.Locator("[data-probe-cue]");
        var probeId = (await original.GetAttributeAsync("data-probe-cue"))!;
        var pattern = (await original.GetAttributeAsync("data-probe-pattern"))!;
        var appearance = (await original.GetAttributeAsync("data-probe-appearance"))!;
        var label = await original.InnerTextAsync();
        await workbench.Probes.First.GetByTitle("Move down", new() { Exact = true }).ClickAsync();
        var moved = workbench.Probes.Last.Locator("[data-probe-cue]");
        await Expect(moved).ToHaveAttributeAsync("data-probe-cue", probeId);
        await Expect(moved).ToHaveAttributeAsync("data-probe-pattern", pattern);
        await Expect(moved).ToHaveAttributeAsync("data-probe-appearance", appearance);
        await Expect(moved).ToHaveTextAsync(label);

        await workbench.Probes.Last.GetByTitle("Find on circuit", new() { Exact = true }).ClickAsync();
        await Expect(workbench.Canvas).ToBeInViewportAsync();
        await workbench.Canvas.ClickAsync(new() { Trial = true });
        var inspector = Page.Locator("[data-selection-inspector] [data-probe-cue]");
        await Expect(inspector).ToHaveAttributeAsync("data-probe-cue", probeId);
        await Expect(inspector).ToHaveAttributeAsync("data-probe-pattern", pattern);
        await Expect(inspector).ToHaveAttributeAsync("data-probe-appearance", appearance);
        await Expect(inspector).ToHaveTextAsync(label);
        await workbench.WaveformClose.ClickAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Diagnostics", Exact = true }).ClickAsync();
        await workbench.OpenInspectorAsync();
        await Page.Locator("[data-observe-probe]").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Waveform", Exact = true }))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Expect(workbench.Probes.Last).ToBeInViewportAsync();
        await Expect(workbench.Probes.Last).ToHaveAttributeAsync("data-probe-observed", "true");
    }

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task Diagnostics_UnconnectedPort_SelectsItsSourceAndClearsAfterUndo(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: width);
        await workbench.OpenLibraryAsync();
        var output = Page.Locator("[data-place-option='library:logiclab.core:sink.output']");
        await output.ClickAsync();
        await Expect(output).ToHaveAttributeAsync("aria-pressed", "true");
        await workbench.Canvas.ClickAsync();
        await Expect(output).ToHaveAttributeAsync("aria-pressed", "false");
        await workbench.Compile.ClickAsync();

        var diagnostics = Page.Locator("[data-diagnostics]");
        await Expect(diagnostics).ToBeVisibleAsync();
        await Expect(diagnostics.Locator("[data-diagnostic-code='compiler_required_terminal_unconnected']"))
            .ToHaveCountAsync(1);
        await workbench.OpenInspectorAsync();
        await Page.Locator("[data-instruments-toggle]").ClickAsync();
        await Expect(workbench.Canvas).ToBeHiddenAsync();
        await diagnostics.Locator("[data-diagnostic-reveal]").ClickAsync();
        await Expect(workbench.Canvas).ToBeInViewportAsync();
        await workbench.Canvas.ClickAsync(new() { Trial = true });
        await Expect(Page.Locator("[data-selection-item] h3")).ToHaveTextAsync("Output · D");
        await Expect(Page.Locator("[data-selection-inspector]"))
            .ToContainTextAsync("Connect this required terminal to a net.");

        await workbench.Undo.ClickAsync();
        await Expect(diagnostics.Locator("[data-diagnostic-code]")).ToHaveCountAsync(0);
        await Expect(diagnostics).ToContainTextAsync("Compile the circuit");
        await Assert.That(await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth <= window.innerWidth")).IsTrue();
    }

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task Diagnostics_UnknownInput_TracksCauseAndPreservesWaveformPreferences(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync(width: width);
        await workbench.StartSimulation.ClickAsync();
        await workbench.WaveformSummary.ClickAsync();
        await workbench.OpenInspectorAsync();
        var input = Page.Locator("[data-input-stimulus]").GetByRole(AriaRole.Textbox);
        await input.FillAsync("X");
        await workbench.ApplyInputsAsync();
        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Diagnostics", Exact = true }).ClickAsync();
        var diagnostics = Page.Locator("[data-diagnostics]");
        await Expect(diagnostics.Locator("[data-diagnostic-code='simulation_unknown_driver']"))
            .ToHaveCountAsync(2);
        await Page.Locator("[data-instruments-toggle]").ClickAsync();
        await Expect(workbench.Canvas).ToBeHiddenAsync();
        await diagnostics.Locator("[data-diagnostic-reveal]").Last.ClickAsync();
        await Expect(workbench.Canvas).ToBeInViewportAsync();
        await workbench.Canvas.ClickAsync(new() { Trial = true });
        await Expect(Page.Locator("[data-selection-inspector]"))
            .ToContainTextAsync("An unknown driver contributes X to this net.");

        await Page.GetByRole(AriaRole.Tab, new() { Name = "Waveform", Exact = true }).ClickAsync();
        await Expect(workbench.WaveformSummary).ToHaveAttributeAsync("pressed", "");
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("X");
        await workbench.OpenInspectorAsync();
        await input.FillAsync("0");
        await workbench.ApplyInputsAsync();
        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("2");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Diagnostics", Exact = true }).ClickAsync();
        await Expect(diagnostics.Locator("[data-diagnostic-code]")).ToHaveCountAsync(0);
        await Expect(diagnostics).ToContainTextAsync("No diagnostics");
        await Expect(Page.Locator("[data-selection-inspector]"))
            .Not.ToContainTextAsync("An unknown driver contributes X to this net.");
    }

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task WorkbenchHistory_PlaceUndoRedoAndBranch_UpdatesCircuitAndCommandAvailability(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: width);
        var undo = Page.Locator("[data-command='undo']");
        var redo = Page.Locator("[data-command='redo']");
        var componentCount = Page.Locator(".circuit-summary dd").First;
        await Expect(Page.Locator(".workflow-hint"))
            .ToHaveTextAsync("Place components and connect their ports.");
        await Expect(undo).ToHaveAttributeAsync("disabled", "");
        await Expect(redo).ToHaveAttributeAsync("disabled", "");

        await PlaceGate("logic.and");
        await Expect(componentCount).ToHaveTextAsync("1");
        await workbench.Undo.ClickAsync();
        await Expect(componentCount).ToHaveTextAsync("0");
        await Expect(undo).ToHaveAttributeAsync("disabled", "");
        await workbench.Redo.ClickAsync();
        await Expect(componentCount).ToHaveTextAsync("1");
        await Expect(redo).ToHaveAttributeAsync("disabled", "");

        await workbench.Undo.ClickAsync();
        await Expect(componentCount).ToHaveTextAsync("0");
        await PlaceGate("logic.or");
        await Expect(componentCount).ToHaveTextAsync("1");
        await Expect(redo).ToHaveAttributeAsync("disabled", "");
        await Assert.That(await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth <= window.innerWidth")).IsTrue();

        async Task PlaceGate(string contract)
        {
            await workbench.OpenLibraryAsync();
            var option = Page.Locator($"[data-place-option='library:logiclab.core:{contract}']");
            await option.ClickAsync();
            await Expect(option).ToHaveAttributeAsync("aria-pressed", "true");
            await workbench.Canvas.ClickAsync();
            await Expect(option).ToHaveAttributeAsync("aria-pressed", "false");
            await workbench.Canvas.PressAsync("Escape");
        }
    }

    [Test]
    public async Task ComponentPalette_SearchAndPlace_FiltersAndEnablesCompilation()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync();

        await workbench.OpenLibraryAsync();
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
    [Arguments(1280)]
    [Arguments(390)]
    public async Task InverterStarter_StepRestartAndClose_PreservesCircuitAndResetsSimulation(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync(width: width);
        var session = workbench.StartSimulation;
        await Expect(session).ToBeEnabledAsync();

        await session.ClickAsync();
        await workbench.SetAllInputsHighAsync();
        var stimulus = workbench.ApplyInputs;
        await Expect(stimulus).ToBeEnabledAsync();
        await Expect(workbench.ProbeTool).ToBeEnabledAsync();
        await Expect(workbench.Probes).ToHaveCountAsync(1);
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
        await Expect(workbench.LogicalTime).ToHaveTextAsync("0");

        await workbench.ApplyInputsAsync();
        var step = workbench.Step;
        await Expect(stimulus).ToBeVisibleAsync();
        await Expect(step).ToBeVisibleAsync();

        await step.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("1");
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("0");
        await Expect(stimulus).ToBeVisibleAsync();
        await Expect(step).ToBeVisibleAsync();

        await workbench.RestartSimulation.ClickAsync();
        await Expect(workbench.LogicalTime).ToHaveTextAsync("0");
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();
        await Expect(step).ToBeVisibleAsync();
        await Expect(stimulus).ToBeVisibleAsync();

        await workbench.CloseSimulation.ClickAsync();
        await Expect(workbench.StartSimulation).ToBeVisibleAsync();
        await Expect(workbench.WaveformCanvas).Not.ToBeVisibleAsync();
        await Expect(workbench.Canvas).ToBeVisibleAsync();

        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();
        await Expect(workbench.Probes).ToHaveCountAsync(1);
        await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
        await Expect(workbench.LogicalTime).ToHaveTextAsync("0");
    }

    [Test]
    [Arguments(1280)]
    [Arguments(390)]
    public async Task ClockCircuit_RunAndPause_UpdatesTimeAndKeepsControlsReachable(int width)
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenSandboxAsync(width: width);
        await workbench.OpenLibraryAsync();
        await workbench.ComponentSearch.FillAsync("clock");
        var clock = Page.Locator("[data-place-option='library:logiclab.core:source.clock']");
        await clock.ClickAsync();
        // A browser click completes before the server acknowledges the selected tool.
        await Expect(clock).ToHaveAttributeAsync("aria-pressed", "true");
        await workbench.Canvas.ClickAsync();
        await Expect(clock).ToHaveAttributeAsync("aria-pressed", "false");
        await workbench.Compile.ClickAsync();
        await workbench.StartSimulation.ClickAsync();

        await Expect(workbench.Step).ToBeEnabledAsync();
        await workbench.Run.ClickAsync();
        await Expect(workbench.SimulationStatus).ToHaveTextAsync("Running");
        await Expect(workbench.LogicalTime).Not.ToHaveTextAsync("0");
        await Expect(workbench.Pause).ToBeInViewportAsync();
        await workbench.Pause.ClickAsync();
        await Expect(workbench.SimulationStatus).ToHaveTextAsync("Paused");
        await Expect(workbench.Run).ToBeEnabledAsync();
        await Expect(workbench.Step).ToBeEnabledAsync();
        await Expect(workbench.Pause).ToBeHiddenAsync();
        var pausedTime = await workbench.LogicalTime.TextContentAsync();
        await workbench.Step.ClickAsync();
        await Expect(workbench.LogicalTime).Not.ToHaveTextAsync(pausedTime!);
        await Assert.That(await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth <= window.innerWidth")).IsTrue();
    }

    [Test]
    public async Task LogicAnalyzer_HistorySummaryCursorAndLiveFollow_StayCoherent()
    {
        var workbench = new WorkbenchTestPage(Page, application.EditorUri);
        await workbench.OpenExampleAsync();
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
        await workbench.OpenExampleAsync(width: 390, height: 844);
        await workbench.StartSimulation.ClickAsync();
        await Expect(workbench.WaveformCanvas).ToBeVisibleAsync();

        await Page.Locator("[data-instruments-toggle]").ClickAsync();
        foreach (var control in new[]
        {
            workbench.WaveformSummary,
            workbench.WaveformZoomIn,
            workbench.WaveformLive,
        })
        {
            await control.ScrollIntoViewIfNeededAsync();
            await Expect(control).ToBeInViewportAsync();
        }
        await workbench.WaveformClose.ScrollIntoViewIfNeededAsync();
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
