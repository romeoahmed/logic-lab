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
        var unrelatedCancelPreservedGesture = await Page.EvaluateAsync<bool>(
            """
            () => {
              window.sceneHandle.canvas.dispatchEvent(new PointerEvent('pointercancel', {
                pointerId: 999, isPrimary: false,
              }));
              return window.sceneHandle.gesture !== null;
            }
            """);
        await Page.Mouse.UpAsync();
        var selected = await Page.EvaluateAsync<bool>(
            "() => window.sceneCalls.some(call => call.name === 'ReceiveSceneIntentAsync')");

        await canvas.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("Enter");
        var keyboardSelection = await Page.EvaluateAsync<string?>(
            "() => window.sceneCalls.filter(call => call.name === 'ReceiveSceneIntentAsync').at(-1)?.args[0]?.sources[0]?.entityId");

        await Page.Keyboard.DownAsync(" ");
        await Page.Mouse.MoveAsync((float)hit.X, (float)hit.Y);
        await Page.Mouse.DownAsync();
        var spaceGesture = await Page.EvaluateAsync<string?>(
            "() => window.sceneHandle.gesture?.tool?.kind");
        await Page.Mouse.UpAsync();
        await Page.Keyboard.UpAsync(" ");

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
        var zoomBeforeControl = await Page.EvaluateAsync<double>(
            "() => window.sceneHandle.viewport.zoom");
        await Page.EvaluateAsync(
            "() => document.querySelector('[data-scene-zoom=\"in\"]').click()");
        var zoomAfterControl = await Page.EvaluateAsync<double>(
            "() => window.sceneHandle.viewport.zoom");

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

        await Page.EvaluateAsync("() => window.sceneHandle.selectedSources.clear()");
        var intentCountBeforeLocalSelection = await Page.EvaluateAsync<int>(
            "() => window.sceneCalls.filter(call => call.name === 'ReceiveSceneIntentAsync').length");
        await Page.Mouse.ClickAsync((float)hit.X, (float)hit.Y);
        var localSelection = await Page.EvaluateAsync<LocalSelectionState>(
            """
            () => ({
              isSelected: window.sceneHandle.selectedSources.has(
                '12:definition-a17:componentInstance1:a0:'),
              intentCount: window.sceneCalls.filter(call =>
                call.name === 'ReceiveSceneIntentAsync').length,
            })
            """);

        await Page.Mouse.MoveAsync(590, 390);
        await Page.Mouse.DownAsync();
        await Page.EvaluateAsync(
            """
            () => document.querySelector('#components-reconnect-modal').dispatchEvent(
              new CustomEvent('components-reconnect-state-changed', {
                detail: { state: 'show' }, bubbles: true,
              }))
            """);
        var localPanGesture = await Page.EvaluateAsync<string?>(
            "() => window.sceneHandle.gesture?.tool?.kind");
        await Page.Mouse.UpAsync();

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsTrue();
            await Assert.That(unrelatedCancelPreservedGesture).IsTrue();
            await Assert.That(selected).IsTrue();
            await Assert.That(keyboardSelection).IsEqualTo("a");
            await Assert.That(spaceGesture).IsEqualTo("pan");
            await Assert.That(afterWheel.X).IsEqualTo(beforeWheel.X).Within(0.000_001);
            await Assert.That(afterWheel.Y).IsEqualTo(beforeWheel.Y).Within(0.000_001);
            await Assert.That(zoomAfterControl).IsGreaterThan(zoomBeforeControl);
            await Assert.That(disconnected.Connected).IsFalse();
            await Assert.That(disconnected.GestureAbsent).IsTrue();
            await Assert.That(localSelection.IsSelected).IsFalse();
            await Assert.That(localSelection.IntentCount).IsEqualTo(intentCountBeforeLocalSelection);
            await Assert.That(localPanGesture).IsEqualTo("pan");
        }
    }


    [Test]
    public async Task Scene_PlaceTool_EmitsOneCompleteVersionedIntent()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();
        await Page.EvaluateAsync(
            """
            () => window.sceneHandle.setTool({
              kind: 'placeComponent',
              target: { kind: 'libraryContract', libraryId: 'logiclab.core', contractId: 'logic.not' },
              parameters: [{ parameterId: 'width', value: { kind: 'unsigned32', value: 1 } }],
              displayName: 'NOT',
              pinned: false,
            })
            """);

        var beforePreview = await Page.EvaluateAsync<string>(
            "() => window.sceneHandle.canvas.toDataURL()");
        await Page.Mouse.MoveAsync(300, 200);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(340, 240);
        await Page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => resolve()))");
        var afterPreview = await Page.EvaluateAsync<string>(
            "() => window.sceneHandle.canvas.toDataURL()");
        await Page.Mouse.UpAsync();
        var intent = await Page.EvaluateAsync<SceneIntentResult>(
            """
            () => {
              const value = window.sceneCalls.filter(call =>
                call.name === 'ReceiveSceneIntentAsync').at(-1)?.args[0];
              return {
                kind: value?.kind,
                sceneVersion: value?.sceneVersion,
                projectionVersion: value?.projectionVersion,
                circuitDefinitionId: value?.circuitDefinitionId,
              };
            }
            """);

        using (Assert.Multiple())
        {
            await Assert.That(intent.Kind).IsEqualTo("placeComponent");
            await Assert.That(intent.SceneVersion).IsEqualTo(1);
            await Assert.That(intent.ProjectionVersion).IsEqualTo(1);
            await Assert.That(intent.CircuitDefinitionId).IsEqualTo("definition-a");
            await Assert.That(afterPreview).IsNotEqualTo(beforePreview);
        }
    }

    [Test]
    public async Task Scene_SelectionAcknowledgmentAndKeyboardActivation_UseAuthoritativePaths()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();
        var result = await Page.EvaluateAsync<SelectionAcknowledgmentResult>(
            """
            async () => {
              const source = window.sceneHandle.published.items[0].source;
              const fallback = document.querySelector('[data-scene-source]');
              let semanticActivations = 0;
              fallback.addEventListener('click', () => semanticActivations++);
              window.sceneHandle.selectSource(source, 'replace');
              await new Promise(resolve => setTimeout(resolve, 0));
              window.sceneHandle.selectSource(source, 'replace');
              await new Promise(resolve => setTimeout(resolve, 0));
              window.sceneHandle.setTool({ kind: 'wire' });
              window.sceneHandle.focusedSource = window.sceneHandle.sourceKeys()[0];
              window.sceneHandle.canvas.focus();
              window.sceneHandle.canvas.dispatchEvent(new KeyboardEvent('keydown', {
                key: 'Enter', bubbles: true,
              }));
              return {
                selectionIntents: window.sceneCalls.filter(call =>
                  call.name === 'ReceiveSceneIntentAsync'
                    && call.args[0]?.kind === 'selectSources').length,
                semanticActivations,
              };
            }
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.SelectionIntents).IsEqualTo(2);
            await Assert.That(result.SemanticActivations).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Scene_SemanticCommit_WaitsForANewerSnapshotBeforeAcceptingAnother()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        var result = await Page.EvaluateAsync<PendingIntentResult>(
            """
            () => {
              const gesture = { sceneVersion: 1, projectionVersion: 1 };
              const net = { circuitDefinitionId: 'definition-a', entityKind: 'net',
                entityId: 'net-a', portId: null };
              const payload = { net: { authoredNet: net,
                hierarchyPath: { entryCircuitDefinitionId: 'definition-a', steps: [] } } };
              const first = window.sceneHandle.emitIntent('toggleProbe', gesture, payload);
              const second = window.sceneHandle.emitIntent('toggleProbe', gesture, payload);
              return {
                first,
                second,
                intentCount: window.sceneCalls.filter(call =>
                  call.name === 'ReceiveSceneIntentAsync').length,
              };
            }
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.First).IsTrue();
            await Assert.That(result.Second).IsFalse();
            await Assert.That(result.IntentCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Scene_WireFromJunctionToTerminal_CommitsToTheJunctionNet()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        var intent = await Page.EvaluateAsync<WireIntentResult>(
            """
            () => {
              const net = { circuitDefinitionId: 'definition-a', entityKind: 'net',
                entityId: 'net-a', portId: null };
              const junction = { circuitDefinitionId: 'definition-a', entityKind: 'junction',
                entityId: 'junction-a', portId: null };
              const start = {
                source: junction,
                item: { source: junction,
                  interaction: { interactionKind: 'junction', net } },
              };
              const end = {
                source: { circuitDefinitionId: 'definition-a', entityKind: 'definitionPort',
                  entityId: 'port-a', portId: null },
                item: { interaction: { interactionKind: 'definitionPort' } },
              };
              window.sceneHandle.hitTest = () => end;
              window.sceneHandle.commitWireGesture({
                hit: start,
                startWorld: { x: 0, y: 0 }, currentWorld: { x: 100, y: 0 },
                sceneVersion: 1, projectionVersion: 1,
              }, false);
              const value = window.sceneCalls.filter(call =>
                call.name === 'ReceiveSceneIntentAsync').at(-1)?.args[0];
              return {
                kind: value?.kind,
                destinationNetId: value?.destinationNet?.entityId,
                terminalKind: value?.terminals?.[0]?.kind,
              };
            }
            """);

        using (Assert.Multiple())
        {
            await Assert.That(intent.Kind).IsEqualTo("commitWire");
            await Assert.That(intent.DestinationNetId).IsEqualTo("net-a");
            await Assert.That(intent.TerminalKind).IsEqualTo("definitionTerminal");
        }
    }

    [Test]
    public async Task Scene_HostRemoval_CancelsGestureAndDestroysResources()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();
        await Page.Mouse.MoveAsync(300, 200);
        await Page.Mouse.DownAsync();

        await Page.EvaluateAsync("() => document.querySelector('#scene-page').remove()");
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
    public async Task Scene_MissingExactFont_RejectsSystemFallback()
    {
        await OpenModulePageAsync();
        await Page.EvaluateAsync(
            """
            async () => {
              document.styleSheets[0].deleteRule(0);
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
              try {
                await window.sceneHandle.measureText([{
                  key: 'measurement-a', text: 'A', fontRole: 'symbol', alignment: 'center',
                  locale: 'en-US', direction: 'ltr',
                }]);
              } catch {
                // The exact self-hosted face is intentionally absent.
              }
            }
            """);

        var failure = await Page.EvaluateAsync<string?>(
            """
            () => window.sceneCalls.find(call =>
              call.name === 'SceneRendererFailedAsync')?.args[0]
            """);

        await Assert.That(failure).IsEqualTo("fontUnavailable");
    }

    [Test]
    public async Task Scene_UnsupportedPackagedGlyph_RejectsSystemFallback()
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
              try {
                await window.sceneHandle.measureText([{
                  key: 'measurement-a', text: '\u903b\u8f91', fontRole: 'symbol', alignment: 'center',
                  locale: 'zh-CN', direction: 'ltr',
                }]);
              } catch {
                // The packaged font intentionally has no CJK cmap entries.
              }
            }
            """);

        var failure = await Page.EvaluateAsync<string?>(
            """
            () => window.sceneCalls.find(call =>
              call.name === 'SceneRendererFailedAsync')?.args[0]
            """);

        await Assert.That(failure).IsEqualTo("fontUnavailable");
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
              hitSource: window.sceneHandle.hitTest({ x: 50, y: 50 })?.source?.entityId,
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.CellCount).IsGreaterThan(0);
            await Assert.That(result.HitSource).IsEqualTo("a");
        }
    }

    [Test]
    public async Task Scene_SpatialIndexBudgetRejected_FailsClosedWithoutSnapshotLoop()
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
            """
            () => window.sceneCalls.some(call =>
              call.name === 'SceneRendererFailedAsync'
                && call.args[0] === 'browserPolicyExhausted')
            """);
        var result = await Page.EvaluateAsync<SpatialBudgetResult>(
            """
            () => ({
              published: window.sceneHandle.published !== null,
              cellCount: window.sceneHandle.spatialIndex.size,
              localUnavailable: window.sceneHandle.canvas.hasAttribute(
                'data-scene-local-unavailable'),
              snapshotRequired: window.sceneCalls.some(call =>
                call.name === 'SceneSnapshotRequiredAsync'),
            })
            """);

        using (Assert.Multiple())
        {
            await Assert.That(result.Published).IsFalse();
            await Assert.That(result.CellCount).IsEqualTo(0);
            await Assert.That(result.LocalUnavailable).IsTrue();
            await Assert.That(result.SnapshotRequired).IsFalse();
        }
    }

    [Test]
    public async Task Scene_NonDrawableSemanticItem_HasNoCanvasTarget()
    {
        await MountSceneAsync();
        await PublishSnapshotAsync();

        var hasTarget = await Page.EvaluateAsync<bool>(
            """
            () => window.sceneHandle.targetBySource({
              circuitDefinitionId: 'definition-a', entityKind: 'net',
              entityId: 'net-without-geometry', portId: null,
            }) !== null
            """);

        await Assert.That(hasTarget).IsFalse();
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
                bounds: { left: 0, top: 0, right: 200, bottom: 100 },
                gridStepPlanUnits: 100, snapStepGridUnits: 1,
                fontFingerprint: window.sceneFontFingerprint,
                items: [{
                  source: { circuitDefinitionId: 'definition-a', entityKind: 'componentInstance',
                    entityId: 'a', portId: null },
                  order: 0, bounds: { left: 20, top: 20, right: 80, bottom: 80 },
                  hasDrawableTarget: true,
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
                  interaction: { interactionKind: 'component', placement: {
                    origin: { x: 0, y: 0 }, quarterTurnsClockwise: 0, reflected: false,
                  } },
                }, {
                  source: { circuitDefinitionId: 'definition-a', entityKind: 'componentInstance',
                    entityId: 'b', portId: null },
                  order: 1, bounds: { left: 120, top: 20, right: 180, bottom: 80 },
                  hasDrawableTarget: true,
                  origin: { x: 0, y: 0 },
                  operations: [{
                    kind: 'stroke', role: 'outline',
                    bounds: { left: 120, top: 20, right: 180, bottom: 80 },
                    commands: [point('move', 120, 20), point('line', 180, 20),
                      point('line', 180, 80), point('line', 120, 80), point('close', 0, 0)],
                    width: 2, dashPattern: [], lineCap: 'round', lineJoin: 'round',
                    miterLimitRatio: 0,
                  }],
                  hitRegions: [{
                    localId: 'body', kind: 'body', sourcePortId: null, shape: 'rect',
                    bounds: { left: 120, top: 20, right: 180, bottom: 80 },
                    center: null, radius: 0, points: null, targetSource: null,
                  }],
                  interaction: { interactionKind: 'component', placement: {
                    origin: { x: 1, y: 0 }, quarterTurnsClockwise: 0, reflected: false,
                  } },
                }, {
                  source: { circuitDefinitionId: 'definition-a', entityKind: 'net',
                    entityId: 'net-without-geometry', portId: null },
                  order: 2, bounds: { left: 0, top: 0, right: 200, bottom: 100 },
                  hasDrawableTarget: false,
                  origin: { x: 0, y: 0 }, operations: [], hitRegions: [],
                  interaction: { interactionKind: 'net', net: {
                    circuitDefinitionId: 'definition-a', entityKind: 'net',
                    entityId: 'net-without-geometry', portId: null,
                  } },
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
        var fontPath = Path.Combine(
            AppContext.BaseDirectory,
            "AtkinsonHyperlegibleNext-Regular.woff2");
        const string page = """
            <!doctype html>
            <style>
              @font-face {
                font-family: "Atkinson Hyperlegible Next";
                font-style: normal;
                font-weight: 400;
                font-display: block;
                src: url("/AtkinsonHyperlegibleNext-Regular.woff2") format("woff2");
              }
              :root { --ll-canvas: #fff; --ll-ink: #172124; --ll-signal: #08788c; }
              #host { width: 600px; height: 400px; }
              canvas {
                --ll-scene-font-family: Atkinson Hyperlegible Next;
                --ll-scene-font-asset: 378aea0f5c1d179f4e0b5382c06bfc87571b98cfcc4fd1352bc979e2e2259c54;
                width: 100%;
                height: 100%;
                font-family: "Atkinson Hyperlegible Next", sans-serif;
              }
            </style>
            <dialog id="components-reconnect-modal"></dialog>
            <main id="stable" data-browser-host-ancestor>
              <div id="scene-page">
                <section id="host">
                  <canvas data-scene-canvas tabindex="0"></canvas>
                  <button type="button" data-scene-zoom="out">-</button>
                  <button type="button" data-scene-zoom="fit">Fit</button>
                  <button type="button" data-scene-zoom="in">+</button>
                  <button data-scene-source="12:definition-a17:componentInstance1:a0:">Component A</button>
                </section>
              </div>
            </main>
            """;
        await Page.RouteAsync($"{Origin}/**", route => route.Request.Url.EndsWith(
                ".woff2",
                StringComparison.Ordinal)
            ? route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "font/woff2",
                Path = fontPath,
            })
            : route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = route.Request.Url.EndsWith(".js", StringComparison.Ordinal)
                    ? "text/javascript"
                    : "text/html",
                Body = route.Request.Url.EndsWith(".js", StringComparison.Ordinal)
                    ? module
                    : page,
            }));
        await Page.GotoAsync($"{Origin}/");
        await Page.EvaluateAsync(
            """
            () => window.scenePolicy = {
              zoomMillionthsMaximum: 4000000,
              policyRevision: 'test-1', policyId: 'logiclab-browser',
              sceneSnapshotRecordCount: 1000, scenePatchRecordCount: 1000,
              semanticIntentBytes: 65536,
              interopBatchBytes: 16384,
              candidateTransferBytes: 1000000, canvasBitmapPixels: 10000000,
              canvasBitmapBytes: 40000000, effectiveDensityMillionths: 3000000,
              zoomMillionthsMinimum: 250000,
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

    private sealed class LocalSelectionState
    {
        public bool IsSelected { get; set; }

        public int IntentCount { get; set; }
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
        public bool Published { get; set; }

        public bool LocalUnavailable { get; set; }

        public bool SnapshotRequired { get; set; }
    }

    private sealed class SelectionAcknowledgmentResult
    {
        public int SelectionIntents { get; set; }

        public int SemanticActivations { get; set; }
    }

    private sealed class BatchRejectionResult
    {
        public int SceneVersion { get; set; }

        public int TransferCount { get; set; }

        public bool SnapshotRequired { get; set; }
    }

    private sealed class SceneIntentResult
    {
        public string? Kind { get; set; }

        public int SceneVersion { get; set; }

        public int ProjectionVersion { get; set; }

        public string? CircuitDefinitionId { get; set; }
    }

    private sealed class PendingIntentResult
    {
        public bool First { get; set; }

        public bool Second { get; set; }

        public int IntentCount { get; set; }
    }

    private sealed class WireIntentResult
    {
        public string? Kind { get; set; }

        public string? DestinationNetId { get; set; }

        public string? TerminalKind { get; set; }
    }
}
