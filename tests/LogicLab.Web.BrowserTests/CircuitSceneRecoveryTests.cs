using LogicLab.Web.Scene;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneRecoveryTests : PageTest
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Transfer_CancelledDuringDigest_DoesNotPublish(bool destroy)
    {
        var scene = await ReadySceneAsync();
        var committed = await Page.EvaluateAsync<bool>("""
            async destroy => {
              const bytes = new TextEncoder().encode(JSON.stringify({
                buildFingerprint: 'build-a', sceneVersion: 2, projectionVersion: 1,
                circuitDefinitionId: 'definition-a', uiCulture: 'en-US',
                baseDirection: 'leftToRight', diagnostics: [],
              }));
              const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
              const digest = [...hash].map(value => value.toString(16).padStart(2, '0')).join('');
              const handle = window.sceneHandle;
              handle.beginTransfer('cancel-during-digest', 'replacement', bytes.length, digest);
              handle.appendTransfer('cancel-during-digest', 0, btoa(String.fromCharCode(...bytes)));
              const pending = handle.commitTransfer('cancel-during-digest');
              if (destroy) handle.destroy();
              else handle.abortTransfer('cancel-during-digest');
              return await pending;
            }
            """, destroy);

        await Assert.That(committed).IsFalse();
        if (!destroy)
        {
            var component = await scene.WorldToPageAsync(50, 50);
            await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);
            await Assert.That((await scene.LatestIntentAsync()).SceneVersion).IsEqualTo(1UL);
        }
    }

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
    public async Task CanvasContextLost_NewGestureWaitsForRestoration()
    {
        var scene = await ReadySceneAsync();
        var point = await scene.WorldToPageAsync(50, 50);
        await Page.Clock.InstallAsync(new ClockInstallOptions());
        await scene.Canvas.DispatchEventAsync("contextlost");

        await Page.Mouse.ClickAsync((float)point.X, (float)point.Y);

        await Assert.That(await scene.CallbackCountAsync("ReceiveSceneIntentAsync"))
            .IsEqualTo(0);
        await scene.Canvas.DispatchEventAsync("contextrestored");
        await Page.Clock.RunForAsync(50);
        await Page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-receive-scene-intent", "1");
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
