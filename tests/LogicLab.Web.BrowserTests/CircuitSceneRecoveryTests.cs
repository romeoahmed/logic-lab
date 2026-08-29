using LogicLab.Web.Scene;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneRecoveryTests : PageTest
{
    [Test]
    public async Task DuplicatePatch_IsRejectedAtomicallyAndRequestsSnapshot()
    {
        var scene = await ReadySceneAsync();

        await scene.TransferAsync(scene.SameVersionPatch(), "patch");

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-renderer-failed",
            "1");
        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-snapshot-required",
            "1");
        var failure = await scene.LatestCallbackArgumentAsync("SceneRendererFailedAsync");
        var component = await scene.WorldToPageAsync(50, 50);
        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);
        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-receive-scene-intent",
            "1");
        var intent = await scene.LatestIntentAsync();

        using (Assert.Multiple())
        {
            await Assert.That(failure.GetString()).IsEqualTo("invalidPatch");
            await Assert.That(intent.SceneVersion).IsEqualTo(1UL);
        }
    }

    [Test]
    public async Task OutOfOrderTransfer_IsRejectedAndPublishedSceneRemainsUsable()
    {
        var scene = await ReadySceneAsync();

        var rejectedSynchronously = await scene.AppendOutOfOrderBatchAsync();

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-renderer-failed",
            "1");
        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-snapshot-required",
            "1");
        var component = await scene.WorldToPageAsync(50, 50);
        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);
        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-receive-scene-intent",
            "1");

        using (Assert.Multiple())
        {
            await Assert.That(rejectedSynchronously).IsTrue();
            await Assert.That((await scene.LatestIntentAsync()).SceneVersion).IsEqualTo(1UL);
        }
    }

    [Test]
    public async Task CanvasContextUnavailable_FailsClosedWithStableReason()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();

        await scene.MountAsync(contextAvailable: false);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-renderer-failed",
            "1");
        await Expect(scene.Canvas).ToHaveAttributeAsync("data-scene-local-unavailable", "");
        var failure = await scene.LatestCallbackArgumentAsync("SceneRendererFailedAsync");
        await Assert.That(failure.GetString()).IsEqualTo("contextUnavailable");
    }

    [Test]
    public async Task CanvasContextLossWithoutRestore_FailsClosedAfterRecoveryWindow()
    {
        var scene = await ReadySceneAsync();

        await scene.Canvas.DispatchEventAsync("contextlost");

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-renderer-failed",
            "1",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 4_000 });
        await Expect(scene.Canvas).ToHaveAttributeAsync("data-scene-local-unavailable", "");
        var failure = await scene.LatestCallbackArgumentAsync("SceneRendererFailedAsync");
        await Assert.That(failure.GetString()).IsEqualTo("contextLost");
    }

    [Test]
    public async Task HostRemoval_DestroysHandleAtThePublicBoundary()
    {
        var scene = await ReadySceneAsync();

        await Page.EvaluateAsync(
            "() => document.querySelector('[data-testid=\"scene-page\"]').remove()");

        await Expect(scene.ScenePage).Not.ToBeAttachedAsync();
        var rejected = await Page.EvaluateAsync<bool>(
            """
            () => {
              try {
                window.sceneHandle.setTool({ kind: 'select' });
                return false;
              } catch {
                return true;
              }
            }
            """);
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task Remount_RecoveryState_RestoresViewportThroughInteropContract()
    {
        var scene = await ReadySceneAsync();
        await scene.Zoom("in").ClickAsync();
        var before = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();

        await scene.RemountAsync(new BrowserSceneRecoveryStateV1([before]));
        await scene.PublishAsync();

        var after = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        using (Assert.Multiple())
        {
            await Assert.That(after.CircuitDefinitionId)
                .IsEqualTo(before.CircuitDefinitionId);
            await Assert.That(after.TranslateX).IsEqualTo(before.TranslateX).Within(0.000_001);
            await Assert.That(after.TranslateY).IsEqualTo(before.TranslateY).Within(0.000_001);
            await Assert.That(after.Zoom).IsEqualTo(before.Zoom).Within(0.000_001);
        }
    }

    private async Task<CircuitSceneTestPage> ReadySceneAsync()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();
        return scene;
    }
}
