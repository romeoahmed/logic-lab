using LogicLab.Web.Scene;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneInteractionTests : PageTest
{
    [Test]
    public async Task PointerSelection_Component_CommitsVersionedIntent()
    {
        var scene = await ReadySceneAsync();
        var component = await scene.WorldToPageAsync(50, 50);

        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-receive-scene-intent",
            "1");
        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<SelectSourcesSceneIntentV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent!.SceneVersion).IsEqualTo(1UL);
            await Assert.That(intent.ProjectionVersion).IsEqualTo(1UL);
            await Assert.That(intent.CircuitDefinitionId).IsEqualTo("definition-a");
            await Assert.That(intent.Sources).IsEquivalentTo([SceneTestSnapshot.SourceA]);
            await Assert.That(intent.SelectionMode).IsEqualTo("replace");
        }
    }

    [Test]
    public async Task PlaceTool_GridPointer_RoundsBeforeApplyingSnapAndConsumesTool()
    {
        var scene = await ReadySceneAsync(snapStepGridUnits: 4);
        await scene.SetToolAsync(new ScenePlaceToolV1(
            new SceneLibraryComponentTargetV1("logiclab.core", "memory.rom"),
            [new SceneParameterBindingV1(
                "initialImage",
                new SceneNewMemoryImageParameterV1(
                    "ROM initialImage",
                    1,
                    2,
                    ["X", "X"]))],
            "ROM",
            pinned: false));
        var point = await scene.WorldToPageAsync(249, 49);

        await Page.Mouse.ClickAsync((float)point.X, (float)point.Y);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-tool-consumed",
            "1");
        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<PlaceComponentSceneIntentV1>();
        var memory = await Assert.That(intent!.Parameters.Single().Value)
            .IsTypeOf<SceneNewMemoryImageParameterV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent.Placement.Origin).IsEqualTo(new SceneGridPointV1(0, 0));
            await Assert.That(intent.Target)
                .IsEqualTo(new SceneLibraryComponentTargetV1(
                    "logiclab.core",
                    "memory.rom"));
            await Assert.That(memory!.Words).IsEquivalentTo(["X", "X"]);
        }
    }

    [Test]
    public async Task WireTool_TerminalDrag_EmitsOrthogonalRouteBetweenPortAnchors()
    {
        var scene = await ReadySceneAsync(gridStepPlanUnits: 10);
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(80, 50);
        var end = await scene.WorldToPageAsync(120, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.Mouse.UpAsync();

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<CommitWireSceneIntentV1>();
        var route = await Assert.That(intent!.RouteAdditions.Single())
            .IsTypeOf<SceneOrthogonalWireRouteV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent.Terminals).Count().IsEqualTo(2);
            await Assert.That(route!.Points).IsEquivalentTo([
                new SceneGridPointV1(8, 5),
                new SceneGridPointV1(12, 5),
            ]);
        }
    }

    [Test]
    public async Task CanvasKeyboardNavigation_CurrentSemanticPage_FocusesAndActivatesTarget()
    {
        var scene = await ReadySceneAsync();
        await scene.Canvas.FocusAsync();

        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(scene.SemanticSource("Component B")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-semantic-source",
            SceneTestSnapshot.SourceB.Key);
        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-semantic-activations",
            "1");
    }

    [Test]
    public async Task ReconnectModal_DisconnectedScene_PansWithoutAuthoringIntent()
    {
        var scene = await ReadySceneAsync();
        var intentCount = await scene.CallbackCountAsync("ReceiveSceneIntentAsync");
        await Page.EvaluateAsync(
            """
            () => document.querySelector('#components-reconnect-modal').dispatchEvent(
              new CustomEvent('components-reconnect-state-changed', {
                detail: { state: 'show' }, bubbles: true,
              }))
            """);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-connection-changed",
            "1");
        var connection = await scene.LatestCallbackArgumentAsync(
            "SceneConnectionChangedAsync");
        var component = await scene.WorldToPageAsync(50, 50);
        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);

        using (Assert.Multiple())
        {
            await Assert.That(connection.GetBoolean()).IsFalse();
            await Assert.That(await scene.CallbackCountAsync("ReceiveSceneIntentAsync"))
                .IsEqualTo(intentCount);
        }
    }

    [Test]
    public async Task Replacement_AutomaticViewport_RefitsExpandedScene()
    {
        var scene = await ReadySceneAsync();
        var expandedBounds = new SceneRect(0, 0, 3_600, 1_400);

        await scene.PublishAsync(sceneVersion: 2, bounds: expandedBounds);
        var component = await scene.WorldToPageAsync(150, 50, expandedBounds);
        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<SelectSourcesSceneIntentV1>();
        await Assert.That(intent!.Sources).IsEquivalentTo([SceneTestSnapshot.SourceB]);
    }

    [Test]
    public async Task AuthoringGesture_Replacement_PreservesTheWorkingViewport()
    {
        var scene = await ReadySceneAsync();
        await scene.PublishAsync(sceneVersion: 2, empty: true);
        await scene.SetToolAsync(new ScenePlaceToolV1(
            new SceneLibraryComponentTargetV1("logiclab.core", "logic.not"),
            [],
            "NOT",
            pinned: false));
        var placement = await scene.WorldToPageAsync(249, 49);

        await Page.Mouse.ClickAsync((float)placement.X, (float)placement.Y);
        var workingViewport = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        await scene.PublishAsync(
            sceneVersion: 3,
            bounds: new SceneRect(0, 0, 3_600, 1_400));

        var recoveredViewport = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        using (Assert.Multiple())
        {
            await Assert.That(workingViewport.Zoom).IsLessThanOrEqualTo(1);
            await Assert.That(recoveredViewport).IsEqualTo(workingViewport);
        }
    }

    [Test]
    public async Task ZoomControls_ManualZoom_PersistsRecoverableViewport()
    {
        var scene = await ReadySceneAsync();
        var automatic = await scene.CaptureRecoveryStateAsync();

        await scene.Zoom("Zoom in").ClickAsync();

        var customized = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        using (Assert.Multiple())
        {
            await Assert.That(automatic.Viewports).IsEmpty();
            await Assert.That(customized.Zoom).IsGreaterThan(0.16);
        }
    }

    private async Task<CircuitSceneTestPage> ReadySceneAsync(
        int snapStepGridUnits = 1,
        int gridStepPlanUnits = 100)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync(
            snapStepGridUnits: snapStepGridUnits,
            gridStepPlanUnits: gridStepPlanUnits);
        return scene;
    }
}
