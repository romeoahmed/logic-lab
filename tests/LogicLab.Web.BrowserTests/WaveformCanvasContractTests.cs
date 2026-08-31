using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class WaveformCanvasContractTests : PageTest
{
    [Test]
    public async Task Snapshot_ExactCoverage_CommitsWithOnePendingFrame()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        var committed = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot());
        await waveform.WaitForFramesAsync();

        using (Assert.Multiple())
        {
            await Assert.That(committed).IsTrue();
            await Assert.That(await waveform.MaximumPendingFramesAsync()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Snapshot_InvalidRecords_AreRejectedWithoutPoisoningNextCommit()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        var coverageHole = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(segmentEndExclusive: "9"));
        var nonzeroPadding = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(vectorData: "BA=="));
        var unresolvedSegment = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(binding: "unresolved"));
        var valid = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot());

        using (Assert.Multiple())
        {
            await Assert.That(coverageHole).IsFalse();
            await Assert.That(nonzeroPadding).IsFalse();
            await Assert.That(unresolvedSegment).IsFalse();
            await Assert.That(valid).IsTrue();
        }
    }

    [Test]
    public async Task Transfer_ChunkBeyondDeclaredLength_IsRejectedBeforeCommit()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        await Assert.That(await waveform.AppendBeyondDeclaredLengthIsRejectedAsync())
            .IsTrue();
    }

    [Test]
    public async Task CursorHitTest_FullUnsignedRange_PreservesIntegerPrecision()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        var endExclusive = ((UInt128)ulong.MaxValue + UInt128.One).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(
                viewportEndExclusive: endExclusive,
                segmentEndExclusive: endExclusive))).IsTrue();
        await waveform.WaitForFramesAsync();

        await waveform.Canvas.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 300, Y = 80 },
        });

        await Assert.That(await waveform.WaitForCursorLogicalTimeAsync())
            .IsEqualTo("9223372036854775808");
    }

    [Test]
    public async Task DeviceDensityChange_ResizesBitmapAndRearmsTheMediaQuery()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        await waveform.ChangeDeviceDensityAsync(2);
        var firstWidth = await waveform.BitmapWidthAsync();
        await waveform.ChangeDeviceDensityAsync(1.5);

        using (Assert.Multiple())
        {
            await Assert.That(firstWidth).IsEqualTo(1_200);
            await Assert.That(await waveform.BitmapWidthAsync()).IsEqualTo(900);
        }
    }

    [Test]
    public async Task ReconnectModal_LocalPanSurvivesReconnectAndNextGesturePublishes()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();
        await waveform.WaitForFramesAsync();

        await Page.Clock.InstallAsync(new ClockInstallOptions());
        await waveform.DispatchReconnectStateAsync("show");
        await waveform.ArmWheelObservationAsync();
        await waveform.Canvas.HoverAsync();
        await Page.Mouse.WheelAsync(0, 100);
        await Page.Clock.RunForAsync(500);

        using (Assert.Multiple())
        {
            await Assert.That(await waveform.WheelWasHandledAsync()).IsTrue();
            await Assert.That(await waveform.IntentCountAsync()).IsEqualTo(0);
        }

        await waveform.DispatchReconnectStateAsync("hide");
        await waveform.ArmWheelObservationAsync();
        await Page.Mouse.WheelAsync(0, -100);
        await Page.Clock.RunForAsync(500);

        using (Assert.Multiple())
        {
            await Assert.That(await waveform.WheelWasHandledAsync()).IsTrue();
            await Assert.That(await waveform.IntentCountAsync()).IsEqualTo(1);
            await Assert.That(await waveform.LastViewportAsync()).IsEqualTo("5:15");
        }
    }

    [Test]
    public async Task ContextRestored_ReacquiresTheContextAndRemainsInteractive()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();

        await Assert.That(await waveform.RestoreContextAsync()).IsTrue();
        await waveform.WaitForFramesAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(waveformVersion: 2))).IsTrue();

        var logicalTime = await waveform.PlacePrimaryCursorAsync(180);
        await Assert.That(ulong.TryParse(
                logicalTime,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                && parsed < 10)
            .IsTrue();
    }

    [Test]
    public async Task PointerGesture_ForeignCancellation_DoesNotDiscardActiveCursorCommit()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();
        await waveform.WaitForFramesAsync();
        var bounds = await waveform.Canvas.BoundingBoxAsync();
        await Assert.That(bounds).IsNotNull();

        await Page.Mouse.MoveAsync(
            checked((float)(bounds!.X + 120)),
            checked((float)(bounds.Y + 80)));
        await Page.Mouse.DownAsync();
        await waveform.DispatchForeignPointerCancellationAsync();
        await Page.Mouse.UpAsync();

        await Assert.That(await waveform.WaitForCursorIntentAsync())
            .IsEqualTo("setCursor|primary");
    }

    [Test]
    public async Task Destroy_DuringCursorGesture_ReleasesCaptureAndPendingFrame()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();
        await waveform.WaitForFramesAsync();
        var bounds = await waveform.Canvas.BoundingBoxAsync();
        await Assert.That(bounds).IsNotNull();
        await Page.Mouse.MoveAsync(
            checked((float)(bounds!.X + 120)),
            checked((float)(bounds.Y + 80)));
        await Page.Mouse.DownAsync();

        var released = await waveform.DestroyActiveGestureAsync();
        await Page.Mouse.UpAsync();

        await Assert.That(released).IsTrue();
    }
}

internal sealed class WaveformCanvasTestPage(IPage page)
{
    private const string BuildFingerprint = "build-a";
    private const string Origin = "https://waveform.logiclab.test";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public ILocator Canvas => page.Locator("[data-waveform-canvas]");

    public async Task OpenAndMountAsync()
    {
        var module = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "LogicAnalyzer.razor.js"));
        await page.RouteAsync($"{Origin}/**", route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            return path == "/Components/Editor/LogicAnalyzer.razor.js"
                ? route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/javascript",
                    Body = module,
                })
                : route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/html",
                    Body = Document,
                });
        });
        await page.GotoAsync($"{Origin}/");
        await page.EvaluateAsync(
            """
            async buildFingerprint => {
              window.receivedWaveformIntents = [];
              const module = await import('/Components/Editor/LogicAnalyzer.razor.js');
              window.waveformHandle = module.mount(
                document.querySelector('[data-waveform-host]'),
                buildFingerprint,
                {
                  policyId: 'logiclab-browser',
                  policyRevision: 'test-1',
                  semanticIntentBytes: 16_384,
                  interopBatchBytes: 16_384,
                  candidateTransferBytes: 1_000_000,
                  canvasBitmapPixels: 10_000_000,
                  effectiveDensityMillionths: 3_000_000,
                },
                {
                  invokeMethodAsync(method, payload) {
                    if (method === 'ReceiveWaveformIntentAsync') {
                      window.receivedWaveformIntents.push(payload);
                    }
                    return Promise.resolve();
                  },
                });
            }
            """,
            BuildFingerprint);
    }

    public async Task<bool> CommitSnapshotAsync(object snapshot)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, WebJson);
        var transferId = $"test-{Guid.CreateVersion7():N}";
        var byteLength = bytes.Length;
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var chunk = Convert.ToBase64String(bytes);
        var request = new
        {
            transferId,
            byteLength,
            digest,
            chunk,
        };
        return await page.EvaluateAsync<bool>(
            """
            async request => {
              window.waveformHandle.beginTransfer(
                request.transferId,
                'snapshot',
                request.byteLength,
                request.digest);
              window.waveformHandle.appendTransfer(
                request.transferId,
                0,
                request.chunk);
              return await window.waveformHandle.commitTransfer(request.transferId);
            }
            """,
            request);
    }

    public async Task WaitForFramesAsync() => await page.EvaluateAsync(
        "() => window.waitForWaveformFrames()");

    public Task<int> MaximumPendingFramesAsync() => page.EvaluateAsync<int>(
        "() => window.maximumPendingWaveformFrames");

    public Task<bool> AppendBeyondDeclaredLengthIsRejectedAsync() =>
        page.EvaluateAsync<bool>(
            """
            async () => {
              const transferId = 'declared-length-test';
              window.waveformHandle.beginTransfer(
                transferId,
                'snapshot',
                1,
                '0000000000000000000000000000000000000000000000000000000000000000');
              try {
                window.waveformHandle.appendTransfer(transferId, 0, 'AAE=');
                return false;
              } catch {
                return !(await window.waveformHandle.commitTransfer(transferId));
              }
            }
            """);

    public Task<int> BitmapWidthAsync() => page.EvaluateAsync<int>(
        "() => document.querySelector('[data-waveform-canvas]').width");

    public Task<int> IntentCountAsync() => page.EvaluateAsync<int>(
        "() => window.receivedWaveformIntents.length");

    public Task<string> LastViewportAsync() => page.EvaluateAsync<string>(
        """
        () => {
          const viewport = window.receivedWaveformIntents.at(-1).viewport;
          return `${viewport.startInclusive}:${viewport.endExclusive}`;
        }
        """);

    public Task DispatchReconnectStateAsync(string state) => page
        .Locator("#components-reconnect-modal")
        .EvaluateAsync(
            """
            (element, reconnectState) => element.dispatchEvent(new CustomEvent(
              'components-reconnect-state-changed',
              { detail: { state: reconnectState } }))
            """,
            state);

    public Task ArmWheelObservationAsync() => page.EvaluateAsync(
        """
        () => {
          window.waveformWheelHandled = new Promise(resolve => {
            document.querySelector('[data-waveform-canvas]').addEventListener(
              'wheel',
              event => queueMicrotask(() => resolve(event.defaultPrevented)),
              { once: true });
          });
        }
        """);

    public Task<bool> WheelWasHandledAsync() => page.EvaluateAsync<bool>(
        "() => window.waveformWheelHandled");

    public async Task ChangeDeviceDensityAsync(double density)
    {
        await page.EvaluateAsync(
            """
            density => {
              Object.defineProperty(window, 'devicePixelRatio', {
                configurable: true,
                value: density,
              });
              window.waveformDensityMedia.dispatchEvent(new Event('change'));
            }
            """,
            density);
        await WaitForFramesAsync();
    }

    public Task<bool> RestoreContextAsync() => page.EvaluateAsync<bool>(
        """
        () => {
          const canvas = document.querySelector('[data-waveform-canvas]');
          const lost = new Event('contextlost', { cancelable: true });
          canvas.dispatchEvent(lost);
          canvas.dispatchEvent(new Event('contextrestored'));
          return lost.defaultPrevented;
        }
        """);

    public async Task DispatchForeignPointerCancellationAsync()
    {
        await Canvas.DispatchEventAsync(
            "pointerdown",
            new Dictionary<string, object>
            {
                ["button"] = 0,
                ["isPrimary"] = false,
                ["pointerId"] = 99,
                ["shiftKey"] = true,
            });
        await Canvas.DispatchEventAsync(
            "pointercancel",
            new Dictionary<string, object>
            {
                ["isPrimary"] = false,
                ["pointerId"] = 99,
            });
    }

    public async Task<string> WaitForCursorIntentAsync()
    {
        await page.WaitForFunctionAsync(
            "() => window.receivedWaveformIntents.length > 0");
        return await page.EvaluateAsync<string>(
            """
            () => {
              const intent = window.receivedWaveformIntents.at(-1);
              return `${intent.kind}|${intent.cursorKind}`;
            }
            """);
    }

    public async Task<string> WaitForCursorLogicalTimeAsync()
    {
        await page.WaitForFunctionAsync(
            "() => window.receivedWaveformIntents.some(intent => intent.kind === 'setCursor')");
        return await page.EvaluateAsync<string>(
            """
            () => window.receivedWaveformIntents
              .findLast(intent => intent.kind === 'setCursor')
              .logicalTime
            """);
    }

    public async Task<string> PlacePrimaryCursorAsync(float x)
    {
        var priorIntentCount = await IntentCountAsync();
        await Canvas.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = x, Y = 80 },
        });
        await page.WaitForFunctionAsync(
            "prior => window.receivedWaveformIntents.length > prior",
            priorIntentCount);
        return await page.EvaluateAsync<string>(
            """
            () => window.receivedWaveformIntents
              .findLast(intent => intent.kind === 'setCursor')
              .logicalTime
            """);
    }

    public Task<bool> DestroyActiveGestureAsync() => page.EvaluateAsync<bool>(
        """
        () => {
          const canvas = document.querySelector('[data-waveform-canvas]');
          const pointerId = window.lastWaveformPointerId;
          const captured = Number.isInteger(pointerId) && canvas.hasPointerCapture(pointerId);
          window.waveformHandle.destroy();
          return captured &&
            !canvas.hasPointerCapture(pointerId) &&
            window.pendingWaveformFrameIds.size === 0;
        }
        """);

    public static object Snapshot(
        string segmentEndExclusive = "10",
        string vectorData = "AA==",
        string viewportEndExclusive = "10",
        string binding = "resolved",
        ulong waveformVersion = 1)
    {
        var traceValue = new
        {
            Width = 1U,
            Encoding = "logic4-2bit-v1",
            Data = vectorData,
        };
        return new
        {
            BuildFingerprint,
            WaveformVersion = waveformVersion,
            ProjectionVersion = 1,
            SessionId = "session-a",
            SessionVersion = 1,
            CompilationArtifactKey = "artifact-a",
            Rows = new[]
            {
                new
                {
                    ProbeId = "probe-a",
                    Width = 1,
                    DisplayOrdinal = 0,
                    AppearanceOrdinal = 0,
                    Pattern = "solid",
                    Binding = binding,
                },
            },
            ViewState = new
            {
                Viewport = new
                {
                    StartInclusive = "0",
                    EndExclusive = viewportEndExclusive,
                },
                PrimaryCursor = (object?)null,
                SecondaryCursor = (object?)null,
            },
            Trace = new
            {
                Kind = "transitions",
                Segments = new[]
                {
                    new
                    {
                        ProbeId = "probe-a",
                        Range = new
                        {
                            StartInclusive = "0",
                            EndExclusive = segmentEndExclusive,
                        },
                        Value = traceValue,
                        TransitionAtStart = false,
                    },
                },
            },
        };
    }

    private const string Document =
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <style>
            :root {
              --ll-canvas: #ffffff;
              --ll-ink: #172a33;
              --ll-muted: #536970;
              --ll-border: #b7c7cc;
              --ll-probe-0: #08788c;
              --ll-probe-1: #b85e3d;
              --ll-probe-2: #6d6ab7;
              --ll-probe-3: #2c8475;
              --ll-waveform-cursor-primary: #b85e3d;
              --ll-waveform-cursor-secondary: #6d6ab7;
            }
          </style>
        </head>
        <body>
          <dialog id="components-reconnect-modal"></dialog>
          <section data-waveform-host style="display:grid;grid-template-columns:180px 600px">
            <aside data-probe-spine style="height:300px;overflow:auto">
              <h3 style="height:30px;margin:0">Signals</h3>
              <div data-waveform-row-track style="height:480px">A</div>
            </aside>
            <canvas data-waveform-canvas style="display:block;width:600px;height:300px"></canvas>
          </section>
          <script>
            const nativeMatchMedia = window.matchMedia.bind(window);
            window.matchMedia = query => {
              window.waveformDensityMedia = nativeMatchMedia(query);
              return window.waveformDensityMedia;
            };
            document.querySelector('[data-waveform-canvas]').addEventListener(
              'pointerdown',
              event => { window.lastWaveformPointerId = event.pointerId; },
              { capture: true });
            window.pendingWaveformFrameIds = new Set();
            window.maximumPendingWaveformFrames = 0;
            const nativeRequestAnimationFrame = window.requestAnimationFrame.bind(window);
            const nativeCancelAnimationFrame = window.cancelAnimationFrame.bind(window);
            window.requestAnimationFrame = callback => {
              let frameId = 0;
              frameId = nativeRequestAnimationFrame(timestamp => {
                window.pendingWaveformFrameIds.delete(frameId);
                callback(timestamp);
              });
              window.pendingWaveformFrameIds.add(frameId);
              window.maximumPendingWaveformFrames = Math.max(
                window.maximumPendingWaveformFrames,
                window.pendingWaveformFrameIds.size);
              return frameId;
            };
            window.cancelAnimationFrame = frameId => {
              window.pendingWaveformFrameIds.delete(frameId);
              nativeCancelAnimationFrame(frameId);
            };
            window.waitForWaveformFrames = () => new Promise(resolve =>
              nativeRequestAnimationFrame(() => nativeRequestAnimationFrame(resolve)));
          </script>
        </body>
        </html>
        """;
}
