using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    [Test]
    public async Task Editor_ClockRun_ObservesTimeAndPausesBeforeResuming()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderClockEditor(context, workspace);
        await ClickAndWaitForState(rendered, "run", () => !IsDisabled(rendered, "pause"));
        await rendered.WaitForStateAsync(() =>
            rendered.Find("[data-status='logical-time'] dd").TextContent != "0");
        var running = await workspace.ReadCurrent();

        using (Assert.Multiple())
        {
            await Assert.That(RunState(rendered)).IsEqualTo("Running");
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
            await Assert.That(IsDisabled(rendered, "run")).IsTrue();
            await Assert.That(IsDisabled(rendered, "stimulus")).IsTrue();
            await Assert.That(IsDisabled(rendered, "restart")).IsTrue();
            await Assert.That(IsDisabled(rendered, "close-session")).IsTrue();
            await Assert.That(rendered.Find("[data-scene-tool='wire']").HasAttribute("disabled"))
                .IsTrue();
            await Assert.That(rendered.Find("[data-scene-tool='probe']").HasAttribute("disabled"))
                .IsTrue();
            await Assert.That(rendered.FindAll("[data-place-option]").All(option =>
                option.HasAttribute("disabled"))).IsTrue();
        }

        await ClickAndWaitForState(rendered, "pause", () => RunState(rendered) == "Paused");
        var paused = await workspace.ReadCurrent();
        await Assert.That(paused.Simulation!.SessionId).IsEqualTo(running.Simulation!.SessionId);
        await Assert.That(paused.Simulation.LogicalTime).IsGreaterThanOrEqualTo(running.Simulation.LogicalTime);
        await ClickAndWaitForState(rendered, "run", () => !IsDisabled(rendered, "pause"));
        var resumed = await workspace.ReadCurrent();
        await Assert.That(resumed.Simulation!.Run.RunGeneration)
            .IsNotEqualTo(paused.Simulation.Run.RunGeneration);
        await ClickAndWaitForState(rendered, "pause", () => RunState(rendered) == "Paused");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Editor_FiniteRun_PausesWhenTheEventQueueIsEmpty(bool scheduleInput)
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderSimulationEditor(context, workspace);
        if (scheduleInput)
        {
            await rendered.Find("[data-command='stimulus']").ClickAsync();
        }

        await ClickAndWaitForState(rendered, "run", () => RunState(rendered) == "Paused");
        var after = await workspace.ReadCurrent();

        using (Assert.Multiple())
        {
            await Assert.That(after.Simulation!.LogicalTime).IsEqualTo(scheduleInput ? 1UL : 0UL);
            await Assert.That(((RunPausedProjection)after.Simulation.Run).PauseReason)
                .IsEqualTo(RunPauseReason.NoScheduledStimulus);
            await Assert.That(rendered.Find(".status-message").TextContent)
                .IsEqualTo("No future events are scheduled.");
            await Assert.That(IsDisabled(rendered, "step")).IsFalse();
            await Assert.That(IsDisabled(rendered, "run")).IsFalse();
        }
    }

    [Test, Timeout(30_000)]
    public async Task Editor_OldRunObservation_CompletesAfterPauseWithoutRestoringRunningState(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingRunObservationWorkspace();
        var rendered = await RenderClockEditor(context, workspace);
        await ClickAndWaitForState(rendered, "run", () => !IsDisabled(rendered, "pause"));
        workspace.BlockNextProjection();
        await workspace.ObservationStarted.WaitAsync(cancellationToken);

        try
        {
            await ClickAndWaitForState(rendered, "pause", () => RunState(rendered) == "Paused");
            var paused = await workspace.ReadCurrent();
            var beforeRelease = rendered.RenderCount;
            workspace.Release();
            await rendered.WaitForStateAsync(() => rendered.RenderCount > beforeRelease);

            using (Assert.Multiple())
            {
                await Assert.That(RunState(rendered)).IsEqualTo("Paused");
                await Assert.That(rendered.FindComponent<CircuitSceneHost>().Instance.ProjectionVersion)
                    .IsEqualTo(paused.ProjectionVersion);
                await Assert.That(IsDisabled(rendered, "run")).IsFalse();
            }
        }
        finally
        {
            workspace.Release();
        }
    }

    [Test, Timeout(30_000)]
    public async Task Editor_DisposedDuringRunObservation_CancelsObservationAndDetaches(
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var workspace = new BlockingRunObservationWorkspace();
        var rendered = await RenderClockEditor(context, workspace);
        await ClickAndWaitForState(rendered, "run", () => !IsDisabled(rendered, "pause"));
        workspace.BlockNextProjection();
        await workspace.ObservationStarted.WaitAsync(cancellationToken);

        await context.DisposeComponentsAsync();

        using (Assert.Multiple())
        {
            await Assert.That(workspace.ObservationCancelled).IsTrue();
            await Assert.That(workspace.DetachCount).IsEqualTo(1);
        }
    }

    private static string RunState(IRenderedComponent<Editor> rendered) =>
        rendered.Find("[data-status='simulation'] dd").TextContent;

    private static async Task<IRenderedComponent<Editor>> RenderClockEditor(
        BunitContext context, TrackingWorkspace workspace)
    {
        var rendered = await RenderAuthoredEditor(context, workspace);
        var before = await workspace.ReadCurrent();
        await rendered.Find("[data-place-option='library:logiclab.core:source.clock']")
            .ClickAsync();
        var sceneHost = rendered.FindComponent<CircuitSceneHost>();
        var tool = (ScenePlaceToolV1)sceneHost.Instance.ActiveTool;
        await rendered.InvokeAsync(() => sceneHost.Instance.OnIntent.InvokeAsync(
            new PlaceComponentSceneIntentV1(
                LogicLabWebBuild.Fingerprint, 1, before.ProjectionVersion,
                before.ProjectRevision.Document.EntryCircuitDefinitionId.Value,
                tool.Target, tool.Parameters,
                new SceneComponentPlacementV1(new SceneGridPointV1(16, 8), 0, false),
                tool.DisplayName, "none")));
        await ClickAndWaitForState(rendered, "compile", () => !IsDisabled(rendered, "session"));
        await ClickAndWaitForState(rendered, "session", () => !IsDisabled(rendered, "stimulus"));
        return rendered;
    }

    private sealed class BlockingRunObservationWorkspace : TrackingWorkspace
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int blockNextProjection;

        public Task ObservationStarted => started.Task;

        public bool ObservationCancelled { get; private set; }

        public void BlockNextProjection() => Interlocked.Exchange(ref blockNextProjection, 1);

        public void Release() => release.TrySetResult();

        public override async Task<WorkspaceReadOutcome> ReadAsync(
            WorkspaceQueryContext context, WorkspaceQuery query, CancellationToken cancellationToken)
        {
            var outcome = await base.ReadAsync(context, query, cancellationToken);
            if (query is ReadProjection && Interlocked.Exchange(ref blockNextProjection, 0) == 1)
            {
                started.TrySetResult();
                try
                {
                    await release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ObservationCancelled = true;
                    throw;
                }
            }

            return outcome;
        }
    }
}
