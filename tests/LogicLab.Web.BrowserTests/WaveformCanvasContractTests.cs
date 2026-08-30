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
    public async Task Snapshot_CoverageHoleOrNonzeroPadding_IsRejectedAtomically()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        var coverageHole = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(segmentEndExclusive: "9"));
        var nonzeroPadding = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(vectorData: "BA=="));

        using (Assert.Multiple())
        {
            await Assert.That(coverageHole).IsFalse();
            await Assert.That(nonzeroPadding).IsFalse();
            await Assert.That(await waveform.PublishedVersionAsync()).IsNull();
        }
    }

    [Test]
    public async Task Snapshot_UnresolvedRowWithMismatchedValueWidth_IsRejected()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        var committed = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(
                binding: "unresolved",
                bindingReason: "sourceMissing",
                vectorWidth: 2));

        using (Assert.Multiple())
        {
            await Assert.That(committed).IsFalse();
            await Assert.That(await waveform.PublishedVersionAsync()).IsNull();
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
        var endExclusive = ulong.MaxValue.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(
                viewportEndExclusive: endExclusive,
                segmentEndExclusive: endExclusive))).IsTrue();

        await Assert.That(await waveform.TimeAtAsync(300))
            .IsEqualTo("9223372036854775807");
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
                  interopBatchBytes: 16_384,
                  candidateTransferBytes: 1_000_000,
                  canvasBitmapPixels: 10_000_000,
                  effectiveDensityMillionths: 3_000_000,
                  zoomMillionthsMinimum: 50_000,
                  zoomMillionthsMaximum: 4_000_000,
                },
                {
                  invokeMethodAsync(method, payload) {
                    if (method === 'ReceiveWaveformIntent') {
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

    public Task<int?> PublishedVersionAsync() => page.EvaluateAsync<int?>(
        "() => window.waveformHandle.published?.waveformVersion ?? null");

    public Task<bool> AppendBeyondDeclaredLengthIsRejectedAsync() =>
        page.EvaluateAsync<bool>(
            """
            () => {
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
                return !window.waveformHandle.transfers.has(transferId);
              }
            }
            """);

    public Task<string> TimeAtAsync(double cssX) => page.EvaluateAsync<string>(
        "cssX => window.waveformHandle.timeAt(cssX).toString()",
        cssX);

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

    public Task<bool> DestroyActiveGestureAsync() => page.EvaluateAsync<bool>(
        """
        () => {
          const canvas = document.querySelector('[data-waveform-canvas]');
          const pointerId = window.waveformHandle.gesture?.pointerId;
          const captured = pointerId !== undefined && canvas.hasPointerCapture(pointerId);
          window.waveformHandle.destroy();
          return captured &&
            !canvas.hasPointerCapture(pointerId) &&
            window.waveformHandle.gesture === null &&
            window.waveformHandle.pendingFrame === 0 &&
            window.pendingWaveformFrameIds.size === 0;
        }
        """);

    public static object Snapshot(
        string segmentEndExclusive = "10",
        string vectorData = "AA==",
        string viewportEndExclusive = "10",
        string binding = "resolved",
        string? bindingReason = null,
        uint vectorWidth = 1)
    {
        var value = new
        {
            Width = vectorWidth,
            Encoding = "logic4-2bit-v1",
            Data = vectorData,
        };
        var traceValue = new
        {
            Width = 1U,
            Encoding = "logic4-2bit-v1",
            Data = vectorData,
        };
        return new
        {
            BuildFingerprint,
            WaveformVersion = 1,
            ProjectionVersion = 1,
            SessionId = "session-a",
            SessionVersion = 1,
            CompilationArtifactKey = "artifact-a",
            UiCulture = "en-US",
            BaseDirection = "leftToRight",
            Rows = new[]
            {
                new
                {
                    ProbeId = "probe-a",
                    Net = new
                    {
                        AuthoredNet = new
                        {
                            CircuitDefinitionId = "main",
                            EntityKind = "net",
                            EntityId = "net-a",
                            PortId = (string?)null,
                        },
                        HierarchyPath = new
                        {
                            EntryCircuitDefinitionId = "main",
                            Steps = Array.Empty<object>(),
                        },
                    },
                    Width = 1,
                    DisplayOrdinal = 0,
                    ShortLabel = "A",
                    Radix = "binary",
                    AppearanceOrdinal = 0,
                    Pattern = "solid",
                    Binding = binding,
                    BindingReason = bindingReason,
                    SceneNavigation = "available",
                    NavigationReason = (string?)null,
                    CurrentValue = value,
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
                LiveFollow = true,
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
                        Sequence = "1",
                        Value = traceValue,
                        TransitionAtStart = false,
                    },
                },
                Gaps = Array.Empty<object>(),
                LatestSequence = "1",
            },
        };
    }

    private const string Document =
        """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"></head>
        <body>
          <section data-waveform-host style="display:grid;grid-template-columns:180px 600px">
            <aside data-probe-spine style="height:300px;overflow:auto">
              <h3 style="height:30px;margin:0">Signals</h3>
              <div data-waveform-row-track style="height:480px">A</div>
            </aside>
            <canvas data-waveform-canvas style="display:block;width:600px;height:300px"></canvas>
          </section>
          <script>
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
