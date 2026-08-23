using Microsoft.Playwright;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneBrowserTests : PageTest
{
    private const string Origin = "https://logiclab.test";

    [Test]
    public async Task Scene_PointerKeyboardWheelAndDisconnect_StayCoherent()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();
        var canvas = Page.Locator("canvas");
        var hit = await Page.EvaluateAsync<ViewportPoint>(
            """
            () => {
              const handle = window.sceneHandle;
              const rect = handle.canvas.getBoundingClientRect();
              return {
                x: rect.left + handle.viewport.x + (50 * handle.viewport.zoom),
                y: rect.top + handle.viewport.y + (50 * handle.viewport.zoom),
              };
            }
            """);

        await Page.Mouse.MoveAsync((float)hit.X, (float)hit.Y);
        await Page.Mouse.DownAsync();
        var captured = await Page.EvaluateAsync<bool>(
            "() => window.sceneHandle.canvas.hasPointerCapture(window.sceneHandle.gesture.pointerId)");
        await Page.Mouse.UpAsync();
        var selected = await Page.EvaluateAsync<bool>(
            "() => window.sceneCalls.some(call => call.name === 'ReceiveSceneIntentAsync')");

        await canvas.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("Enter");
        var beforeWheel = await Page.EvaluateAsync<WorldPoint>(
            "() => window.sceneHandle.screenToWorld({ x: 240, y: 160 })");
        var wheelPoint = await Page.EvaluateAsync<ViewportPoint>(
            """
            () => {
              const rect = window.sceneHandle.canvas.getBoundingClientRect();
              return { x: rect.left + 240, y: rect.top + 160 };
            }
            """);
        await Page.Mouse.MoveAsync((float)wheelPoint.X, (float)wheelPoint.Y);
        await Page.Mouse.WheelAsync(0, -240);
        var afterWheel = await Page.EvaluateAsync<WorldPoint>(
            "() => window.sceneHandle.screenToWorld({ x: 240, y: 160 })");

        await Page.Mouse.MoveAsync((float)hit.X, (float)hit.Y);
        await Page.Mouse.DownAsync();
        await Page.EvaluateAsync(
            """
            () => document.querySelector('#components-reconnect-modal').dispatchEvent(
              new CustomEvent('components-reconnect-state-changed', {
                detail: { state: 'show' }, bubbles: true,
              }))
            """);
        var disconnected = await Page.EvaluateAsync<DisconnectState>(
            "() => ({ connected: window.sceneHandle.connected, gestureAbsent: window.sceneHandle.gesture === null })");
        await Page.Mouse.UpAsync();

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsTrue();
            await Assert.That(selected).IsTrue();
            await Assert.That(afterWheel.X).IsEqualTo(beforeWheel.X).Within(0.000_001);
            await Assert.That(afterWheel.Y).IsEqualTo(beforeWheel.Y).Within(0.000_001);
            await Assert.That(disconnected.Connected).IsFalse();
            await Assert.That(disconnected.GestureAbsent).IsTrue();
        }
    }

    [Test]
    public async Task Scene_HostRemoval_CancelsGestureAndDestroysResources()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();
        await Page.Mouse.MoveAsync(300, 200);
        await Page.Mouse.DownAsync();

        await Page.EvaluateAsync("() => document.querySelector('#host').remove()");
        await Page.WaitForFunctionAsync("() => window.sceneHandle.destroyed");
        var state = await Page.EvaluateAsync<DestroyedState>(
            """
            () => ({
              destroyed: window.sceneHandle.destroyed,
              gestureAbsent: window.sceneHandle.gesture === null,
              pendingFrame: window.sceneHandle.pendingFrame,
              transferCount: window.sceneHandle.transfers.size,
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(state.Destroyed).IsTrue();
            await Assert.That(state.GestureAbsent).IsTrue();
            await Assert.That(state.PendingFrame).IsEqualTo(0);
            await Assert.That(state.TransferCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Scene_ContextUnavailable_ReportsStableFailureWithoutPayload()
    {
        await OpenModulePageAsync();
        await Page.EvaluateAsync(
            """
            async () => {
              HTMLCanvasElement.prototype.getContext = () => null;
              const module = await import('/CircuitSceneHost.razor.js');
              window.sceneCalls = [];
              const sink = {
                invokeMethodAsync(name, ...args) {
                  window.sceneCalls.push({ name, args });
                  return Promise.resolve();
                },
              };
              window.sceneHandle = module.mount(
                document.querySelector('#host'), 'build-a', window.scenePolicy, sink);
              await new Promise(resolve => setTimeout(resolve, 0));
            }
            """);

        var failure = await Page.EvaluateAsync<string?>(
            """
            () => window.sceneCalls.find(call => call.name === 'SceneRendererFailedAsync')?.args[0]
            """);

        await Assert.That(failure).IsEqualTo("contextUnavailable");
    }

    [Test]
    public async Task Scene_DuplicatePatch_IsAtomicAndRequestsCompleteSnapshot()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        await Page.EvaluateAsync(
            """
            () => {
              const current = window.sceneHandle.published;
              const item = current.items[0];
              window.sceneHandle.apply({
                buildFingerprint: current.buildFingerprint,
                baseSceneVersion: 1,
                nextSceneVersion: 2,
                projectionVersion: 2,
                circuitDefinitionId: current.circuitDefinitionId,
                uiCulture: current.uiCulture,
                baseDirection: current.baseDirection,
                schematicProjectionKey: 'projection-b',
                bounds: current.bounds,
                gridStepPlanUnits: current.gridStepPlanUnits,
                snapStepGridUnits: current.snapStepGridUnits,
                fontFingerprint: current.fontFingerprint,
                itemUpserts: [item, item],
                itemRemovals: [],
                overlayUpserts: [],
                overlayRemovals: [],
              });
            }
            """);
        await Page.WaitForFunctionAsync(
            "() => window.sceneCalls.some(call => call.name === 'SceneSnapshotRequiredAsync')");
        var result = await Page.EvaluateAsync<PatchResult>(
            """
            () => ({
              sceneVersion: window.sceneHandle.published.sceneVersion,
              invalidPatch: window.sceneCalls.some(call =>
                call.name === 'SceneRendererFailedAsync' && call.args[0] === 'invalidPatch'),
              snapshotRequired: window.sceneCalls.some(call => call.name === 'SceneSnapshotRequiredAsync'),
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.SceneVersion).IsEqualTo(1);
            await Assert.That(result.InvalidPatch).IsTrue();
            await Assert.That(result.SnapshotRequired).IsTrue();
        }
    }

    [Test]
    public async Task Scene_Publish_BuildsBoundedSpatialIndexBeforeHitTesting()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        var result = await Page.EvaluateAsync<SpatialIndexResult>(
            """
            () => ({
              cellCount: window.sceneHandle.spatialIndex.size,
              hitSource: window.sceneHandle.hitTest({ x: 50, y: 50 })?.source?.id,
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.CellCount).IsGreaterThan(0);
            await Assert.That(result.HitSource).IsEqualTo("component:a");
        }
    }

    [Test]
    public async Task Scene_SpatialIndexBudgetRejected_OldSceneRemainsPublished()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        await Page.EvaluateAsync(
            """
            async () => {
              const current = window.sceneHandle.published;
              const item = structuredClone(current.items[0]);
              item.bounds = { left: 0, top: 0, right: 10_000_000, bottom: 100 };
              item.hitRegions[0].bounds = item.bounds;
              const patch = {
                buildFingerprint: current.buildFingerprint,
                baseSceneVersion: current.sceneVersion,
                nextSceneVersion: 2,
                projectionVersion: 2,
                circuitDefinitionId: current.circuitDefinitionId,
                uiCulture: current.uiCulture,
                baseDirection: current.baseDirection,
                schematicProjectionKey: 'projection-b',
                bounds: { left: 0, top: 0, right: 10_000_000, bottom: 100 },
                gridStepPlanUnits: current.gridStepPlanUnits,
                snapStepGridUnits: current.snapStepGridUnits,
                fontFingerprint: current.fontFingerprint,
                itemUpserts: [item], itemRemovals: [],
                overlayUpserts: [], overlayRemovals: [],
              };
              const bytes = new TextEncoder().encode(JSON.stringify(patch));
              const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))]
                .map(value => value.toString(16).padStart(2, '0')).join('');
              window.sceneHandle.beginTransfer('oversized-index', 'patch', bytes.length, digest);
              window.sceneHandle.appendTransfer(
                'oversized-index', 0, btoa(String.fromCharCode(...bytes)));
              await window.sceneHandle.commitTransfer('oversized-index');
            }
            """);
        await Page.WaitForFunctionAsync(
            "() => window.sceneCalls.some(call => call.name === 'SceneSnapshotRequiredAsync')");
        var result = await Page.EvaluateAsync<SpatialBudgetResult>(
            """
            () => ({
              sceneVersion: window.sceneHandle.published.sceneVersion,
              cellCount: window.sceneHandle.spatialIndex.size,
              hitSource: window.sceneHandle.hitTest({ x: 50, y: 50 })?.source?.id,
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.SceneVersion).IsEqualTo(1);
            await Assert.That(result.CellCount).IsGreaterThan(0);
            await Assert.That(result.HitSource).IsEqualTo("component:a");
        }
    }

    [Test]
    public async Task Scene_ContextLossWithoutRestore_ReportsStableFailure()
    {
        await MountSceneAsync();
        await Page.EvaluateAsync(
            """
            () => window.sceneHandle.canvas.dispatchEvent(
              new Event('contextlost', { cancelable: true }))
            """);

        await Page.WaitForFunctionAsync(
            """
            () => window.sceneCalls.some(call =>
              call.name === 'SceneRendererFailedAsync' && call.args[0] === 'contextLost')
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 3_000 });

        var contextIsLost = await Page.EvaluateAsync<bool>(
            "() => window.sceneHandle.contextIsLost");
        await Assert.That(contextIsLost).IsTrue();
    }

    [Test]
    public async Task Scene_OutOfOrderPrivateBatch_IsRejectedWithoutChangingScene()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        await Page.EvaluateAsync(
            """
            async () => {
              const bytes = new TextEncoder().encode('{}');
              const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))]
                .map(value => value.toString(16).padStart(2, '0')).join('');
              window.sceneHandle.beginTransfer('out-of-order', 'patch', bytes.length, digest);
              try {
                window.sceneHandle.appendTransfer(
                  'out-of-order', 1, btoa(String.fromCharCode(...bytes)));
              } catch {
                // The public interop call rejects while the stable callback remains payload-free.
              }
            }
            """);
        await Page.WaitForFunctionAsync(
            """
            () => window.sceneCalls.some(call =>
              call.name === 'SceneRendererFailedAsync' && call.args[0] === 'invalidBatch')
            """);
        var result = await Page.EvaluateAsync<BatchRejectionResult>(
            """
            () => ({
              sceneVersion: window.sceneHandle.published.sceneVersion,
              transferCount: window.sceneHandle.transfers.size,
              snapshotRequired: window.sceneCalls.some(call =>
                call.name === 'SceneSnapshotRequiredAsync'),
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.SceneVersion).IsEqualTo(1);
            await Assert.That(result.TransferCount).IsEqualTo(0);
            await Assert.That(result.SnapshotRequired).IsTrue();
        }
    }

    private async Task MountSceneAsync()
    {
        await OpenModulePageAsync();
        await Page.EvaluateAsync(
            """
            async () => {
              const module = await import('/CircuitSceneHost.razor.js');
              window.sceneCalls = [];
              const sink = {
                invokeMethodAsync(name, ...args) {
                  window.sceneCalls.push({ name, args });
                  return Promise.resolve();
                },
              };
              window.sceneHandle = module.mount(
                document.querySelector('#host'), 'build-a', window.scenePolicy, sink);
              const measurements = await window.sceneHandle.measureText([{
                key: 'measurement-a', text: 'A', fontRole: 'symbol', alignment: 'center',
                locale: 'en-US', direction: 'ltr',
              }]);
              window.sceneFontFingerprint = measurements.fontFingerprint;
            }
            """);
    }

    private async Task PublishSnapshotAsync()
    {
        await Page.EvaluateAsync(
            """
            async () => {
              const point = (kind, x, y) => ({
                kind, x, y, control1X: 0, control1Y: 0, control2X: 0, control2Y: 0,
              });
              const snapshot = {
                buildFingerprint: 'build-a', sceneVersion: 1, projectionVersion: 1,
                circuitDefinitionId: 'definition-a', uiCulture: 'en-US',
                baseDirection: 'leftToRight', schematicProjectionKey: 'projection-a',
                bounds: { left: 0, top: 0, right: 100, bottom: 100 },
                gridStepPlanUnits: 100, snapStepGridUnits: 1,
                fontFingerprint: window.sceneFontFingerprint,
                items: [{
                  source: { circuitDefinitionId: 'definition-a', kind: 'component', id: 'component:a' },
                  order: 0, bounds: { left: 20, top: 20, right: 80, bottom: 80 },
                  origin: { x: 0, y: 0 },
                  operations: [{
                    kind: 'stroke', role: 'outline',
                    bounds: { left: 20, top: 20, right: 80, bottom: 80 },
                    commands: [point('move', 20, 20), point('line', 80, 20),
                      point('line', 80, 80), point('line', 20, 80), point('close', 0, 0)],
                    width: 2, dashPattern: [], lineCap: 'round', lineJoin: 'round',
                    miterLimitRatio: 0,
                  }],
                  hitRegions: [{
                    localId: 'body', kind: 'body', sourcePortId: null, shape: 'rect',
                    bounds: { left: 20, top: 20, right: 80, bottom: 80 },
                    center: null, radius: 0, points: null, targetSource: null,
                  }],
                }], overlays: [],
              };
              const bytes = new TextEncoder().encode(JSON.stringify(snapshot));
              const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))]
                .map(value => value.toString(16).padStart(2, '0')).join('');
              const base64 = btoa(String.fromCharCode(...bytes));
              window.sceneHandle.beginTransfer('transfer-a', 'replacement', bytes.length, digest);
              window.sceneHandle.appendTransfer('transfer-a', 0, base64);
              await window.sceneHandle.commitTransfer('transfer-a');
            }
            """);
        await Page.WaitForFunctionAsync("() => window.sceneHandle.published?.sceneVersion === 1");
    }

    private async Task OpenModulePageAsync()
    {
        var module = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "CircuitSceneHost.razor.js"));
        await Page.RouteAsync($"{Origin}/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = route.Request.Url.EndsWith(".js", StringComparison.Ordinal)
                ? "text/javascript"
                : "text/html",
            Body = route.Request.Url.EndsWith(".js", StringComparison.Ordinal)
                ? module
                : """
                  <!doctype html>
                  <style>
                    :root { --ll-canvas: #fff; --ll-ink: #172124; --ll-signal: #08788c; }
                    #host { width: 600px; height: 400px; }
                    canvas { width: 100%; height: 100%; font-family: sans-serif; }
                  </style>
                  <dialog id="components-reconnect-modal"></dialog>
                  <main id="stable"><section id="host"><canvas data-scene-canvas tabindex="0"></canvas></section></main>
                  """,
        }));
        await Page.GotoAsync($"{Origin}/");
        await Page.EvaluateAsync(
            """
            () => window.scenePolicy = {
              policyId: 'logiclab-browser', policyRevision: 'test-1',
              semanticIntentBytes: 65536,
              sceneSnapshotRecordCount: 1000, scenePatchRecordCount: 1000,
              interopBatchBytes: 16384,
              candidateTransferBytes: 1000000, canvasBitmapPixels: 10000000,
              canvasBitmapBytes: 40000000, effectiveDensityMillionths: 3000000,
              zoomMillionthsMinimum: 250000, zoomMillionthsMaximum: 4000000,
              semanticTreePageItems: 200, displayListBytes: 1000000,
              spatialIndexBytes: 1000000, sceneCacheBytes: 4000000,
              waveformCacheBytes: 4000000,
            }
            """);
    }

    private sealed class ViewportPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class WorldPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class DisconnectState
    {
        public bool Connected { get; set; }

        public bool GestureAbsent { get; set; }
    }

    private sealed class DestroyedState
    {
        public bool Destroyed { get; set; }

        public bool GestureAbsent { get; set; }

        public int PendingFrame { get; set; }

        public int TransferCount { get; set; }
    }

    private sealed class PatchResult
    {
        public int SceneVersion { get; set; }

        public bool InvalidPatch { get; set; }

        public bool SnapshotRequired { get; set; }
    }

    private class SpatialIndexResult
    {
        public int CellCount { get; set; }

        public string? HitSource { get; set; }
    }

    private sealed class SpatialBudgetResult : SpatialIndexResult
    {
        public int SceneVersion { get; set; }
    }

    private sealed class BatchRejectionResult
    {
        public int SceneVersion { get; set; }

        public int TransferCount { get; set; }

        public bool SnapshotRequired { get; set; }
    }
}
