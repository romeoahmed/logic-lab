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
    public async Task ZoomControls_AccessibleButtons_UpdateRecoverableViewport()
    {
        var scene = await ReadySceneAsync();
        var before = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();

        await scene.Zoom("Zoom in").ClickAsync();

        var after = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        await Assert.That(after.Zoom).IsGreaterThan(before.Zoom);
    }

    private async Task<CircuitSceneTestPage> ReadySceneAsync(int snapStepGridUnits = 1)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync(snapStepGridUnits: snapStepGridUnits);
        return scene;
    }
}
