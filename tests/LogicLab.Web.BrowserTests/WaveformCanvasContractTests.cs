using System.Text.Json;
using Microsoft.Playwright;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class WaveformCanvasContractTests : PageTest
{
    [Test]
    public async Task Transfer_BorrowedBinaryView_OwnsBytesAfterAppendReturns()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(), reuseBuffer: true)).IsTrue();
        await waveform.WaitForFramesAsync();

        await Assert.That(await waveform.PlacePrimaryCursorAsync(300)).IsEqualTo("5");
    }

    [Test]
    [Arguments("0", "1")]
    [Arguments("0", "3")]
    [Arguments("0", "7")]
    [Arguments("18446744073709551610", "18446744073709551616")]
    [Arguments("18446744073709551000", "18446744073709551616")]
    public async Task Ruler_ShortOrLargeLogicalTimes_DrawsDistinctAccurateNonoverlappingLabels(string start, string end)
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await waveform.Canvas.EvaluateAsync("""
            canvas => {
              const context = canvas.getContext('2d');
              const fillRect = context.fillRect.bind(context);
              const fillText = context.fillText.bind(context);
              window.rulerLabels = [];
              context.fillRect = (x, y, width, height) => {
                if (x === 0 && y === 0) window.rulerLabels = [];
                fillRect(x, y, width, height);
              };
              context.fillText = (text, x, y, ...args) => {
                if (y === 15) {
                  const width = context.measureText(text).width;
                  const left = x - (context.textAlign === 'right' ? width : context.textAlign === 'center' ? width / 2 : 0);
                  window.rulerLabels.push({ text, x, left, right: left + width });
                }
                fillText(text, x, y, ...args);
              };
            }
            """);
        var snapshot = JsonSerializer.SerializeToNode(
            WaveformCanvasTestPage.Snapshot(segmentEndExclusive: end, viewportEndExclusive: end),
            JsonSerializerOptions.Web)!;
        snapshot["viewState"]!["viewport"]!["startInclusive"] = start;
        snapshot["trace"]!["segments"]![0]!["range"]!["startInclusive"] = start;
        await Assert.That(await waveform.CommitSnapshotAsync(snapshot)).IsTrue();
        await waveform.WaitForFramesAsync();

        await Assert.That(await Page.EvaluateAsync<bool>("""
            ({ start, end }) => {
              const labels = window.rulerLabels;
              const width = document.querySelector('[data-waveform-canvas]').getBoundingClientRect().width;
              const span = BigInt(end) - BigInt(start);
              return labels.length >= 2 && labels[0].text === start && labels.at(-1).text === end
                && new Set(labels.map(label => label.text)).size === labels.length
                && labels.every((label, index) => {
                  const ratio = Number(BigInt(label.text) - BigInt(start)) / Number(span);
                  return Math.abs(label.x - ratio * width) < 0.01
                    && (index === 0 || labels[index - 1].right < label.left);
                });
            }
            """, new { start, end })).IsTrue();
    }

    [Test]
    public async Task PageRemoval_WithoutDotNetDisposal_DestroysNestedHandle()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await waveform.WaitForFramesAsync();

        await Page.Locator("[data-waveform-page]").EvaluateAsync("element => element.remove()");
        await waveform.WaitForFramesAsync();

        await Assert.That(await Page.EvaluateAsync<bool>("""
            () => {
              try {
                window.waveformHandle.setInteractionMode('commitEnabled');
                return false;
              } catch {
                return window.waveformHandle.destroyed;
              }
            }
            """)).IsTrue();
    }

    [Test]
    public async Task ProbeRows_DomReordersBeforeSnapshot_KeepsTraceWithItsProbe()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Page.Locator("[data-probe-spine]").EvaluateAsync("""
            spine => {
              const row = spine.querySelector('[data-waveform-row-track]');
              row.style.height = '100px';
              const next = row.cloneNode(true);
              next.dataset.waveformRowTrack = 'probe-b';
              spine.append(next);
              const context = document.querySelector('[data-waveform-canvas]').getContext('2d');
              const fillText = context.fillText.bind(context);
              window.waveformLabels = [];
              context.fillText = (text, x, y, ...args) => {
                window.waveformLabels.push({ text, y });
                fillText(text, x, y, ...args);
              };
            }
            """);
        var snapshot = JsonSerializer.SerializeToNode(
            WaveformCanvasTestPage.Snapshot(vectorData: "AQ=="), JsonSerializerOptions.Web)!;
        var rows = snapshot["rows"]!.AsArray();
        rows[0]!["width"] = 4;
        var nextRow = rows[0]!.DeepClone();
        nextRow["probeId"] = "probe-b";
        nextRow["displayOrdinal"] = 1;
        rows.Add(nextRow);
        var segments = snapshot["trace"]!["segments"]!.AsArray();
        segments[0]!["value"]!["width"] = 4;
        var nextSegment = segments[0]!.DeepClone();
        nextSegment["probeId"] = "probe-b";
        nextSegment["value"]!["data"] = "BA==";
        segments.Add(nextSegment);
        await Assert.That(await waveform.CommitSnapshotAsync(snapshot)).IsTrue();
        await waveform.WaitForFramesAsync();

        await Page.Locator("[data-probe-spine]").EvaluateAsync("""
            spine => {
              window.waveformLabels = [];
              const rows = spine.querySelectorAll('[data-waveform-row-track]');
              spine.insertBefore(rows[1], rows[0]);
            }
            """);
        await waveform.WaitForFramesAsync();

        await Assert.That(await Page.EvaluateAsync<bool>("""
            () => {
              const first = window.waveformLabels.find(label => label.text === '0001');
              const second = window.waveformLabels.find(label => label.text === '0010');
              return !!first && !!second && second.y < first.y;
            }
            """)).IsTrue();
    }

    [Test]
    public async Task ProbeRows_DomPrecedesSnapshot_RemainsAvailableUntilReplacement()
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();
        await waveform.WaitForFramesAsync();

        await Page.Locator("[data-probe-spine]").EvaluateAsync("""
            spine => {
              const row = spine.querySelector('[data-waveform-row-track]');
              const next = row.cloneNode(true);
              next.dataset.waveformRowTrack = 'probe-b';
              spine.append(next);
              spine.dispatchEvent(new Event('scroll'));
            }
            """);
        await waveform.WaitForFramesAsync();
        var replacement = JsonSerializer.SerializeToNode(
            WaveformCanvasTestPage.Snapshot(waveformVersion: 2), JsonSerializerOptions.Web)!;
        var rows = replacement["rows"]!.AsArray();
        var nextRow = rows[0]!.DeepClone();
        nextRow["probeId"] = "probe-b";
        nextRow["displayOrdinal"] = 1;
        rows.Add(nextRow);
        var segments = replacement["trace"]!["segments"]!.AsArray();
        var nextSegment = segments[0]!.DeepClone();
        nextSegment["probeId"] = "probe-b";
        segments.Add(nextSegment);

        await Assert.That(await waveform.CommitSnapshotAsync(replacement)).IsTrue();
        await waveform.WaitForFramesAsync();
        await Assert.That(await waveform.PlacePrimaryCursorAsync(180)).IsEqualTo("3");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BusLabels_FourStateVectors_PreserveUnknownAndHighImpedance(bool summary)
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await waveform.Canvas.EvaluateAsync("""
            canvas => {
              const context = canvas.getContext('2d');
              const fillText = context.fillText.bind(context);
              window.waveformLabels = [];
              context.fillText = (text, ...args) => {
                window.waveformLabels.push(text);
                fillText(text, ...args);
              };
            }
            """);
        var snapshot = JsonSerializer.SerializeToNode(
            WaveformCanvasTestPage.Snapshot(vectorData: "Sw=="),
            JsonSerializerOptions.Web)!;
        snapshot["rows"]![0]!["width"] = 4;
        var trace = snapshot["trace"]!;
        var segment = trace["segments"]![0]!;
        segment["value"]!["width"] = 4;
        if (summary)
        {
            trace["kind"] = "summary";
            trace["aggregation"] = "logic-envelope-v1";
            segment["firstValue"] = segment["value"]!.DeepClone();
            segment["lastValue"] = segment["value"]!.DeepClone();
            segment["lastValue"]!["data"] = "AQ==";
            segment.AsObject().Remove("value");
            segment.AsObject().Remove("transitionAtStart");
            segment["hadTransition"] = true;
            segment["hadMixedValues"] = true;
        }

        await Assert.That(await waveform.CommitSnapshotAsync(snapshot)).IsTrue();
        await waveform.WaitForFramesAsync();

        var labels = await Page.EvaluateAsync<string[]>("() => window.waveformLabels");
        await Assert.That(labels).Contains(summary ? "10XZ→0001" : "10XZ");
    }

    [Test]
    [Arguments("abort")]
    [Arguments("destroy")]
    public async Task Transfer_CancelledDuringDigest_DoesNotPublish(string cancellation)
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();

        var committed = await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(), cancellation);

        await Assert.That(committed).IsFalse();
        if (cancellation == "abort")
        {
            await Assert.That(await waveform.CommitSnapshotAsync(
                WaveformCanvasTestPage.Snapshot())).IsTrue();
        }
    }

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
    [Arguments(false)]
    [Arguments(true)]
    public async Task CursorGesture_ReleaseCoordinate_UsesFinalClampedLogicalTime(bool fullRange)
    {
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        var endExclusive = fullRange ? "18446744073709551616" : "10";
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(
                viewportEndExclusive: endExclusive,
                segmentEndExclusive: endExclusive))).IsTrue();
        var bounds = await waveform.Canvas.BoundingBoxAsync();
        await Assert.That(bounds).IsNotNull();
        await Page.Mouse.MoveAsync(bounds!.X + 120, bounds.Y + 80);
        await Page.Mouse.DownAsync();

        await waveform.Canvas.EvaluateAsync("""
            (canvas, x) => {
              const bounds = canvas.getBoundingClientRect();
              canvas.dispatchEvent(new PointerEvent('pointerup', {
                pointerId: window.lastWaveformPointerId, button: 0,
                clientX: bounds.left + x, clientY: bounds.top + 80,
              }));
            }
            """, fullRange ? 650 : 480);
        await Page.Mouse.UpAsync();

        await Assert.That(await waveform.WaitForCursorLogicalTimeAsync())
            .IsEqualTo(fullRange ? "18446744073709551615" : "8");
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
        await Page.Clock.InstallAsync(new ClockInstallOptions { TimeDate = DateTime.UnixEpoch });
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();
        await waveform.WaitForFramesAsync();

        await Page.Clock.PauseAtAsync(DateTime.UnixEpoch.AddHours(1));
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
    public async Task ContextLost_PaintAndIntentsWaitForRestoration()
    {
        await Page.Clock.InstallAsync(new ClockInstallOptions { TimeDate = DateTime.UnixEpoch });
        var waveform = new WaveformCanvasTestPage(Page);
        await waveform.OpenAndMountAsync();
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot())).IsTrue();
        await waveform.WaitForFramesAsync();
        await Page.Clock.PauseAtAsync(DateTime.UnixEpoch.AddHours(1));
        await waveform.Canvas.EvaluateAsync("""
            canvas => {
              const context = canvas.getContext('2d');
              const fillRect = context.fillRect.bind(context);
              window.paintCount = 0;
              context.fillRect = (...args) => { window.paintCount++; fillRect(...args); };
              canvas.dispatchEvent(new Event('contextlost'));
            }
            """);
        await Assert.That(await waveform.CommitSnapshotAsync(
            WaveformCanvasTestPage.Snapshot(waveformVersion: 2))).IsTrue();
        await waveform.Canvas.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 180, Y = 80 },
        });
        await waveform.Canvas.PressAsync("ArrowRight");
        await waveform.Canvas.DispatchEventAsync("wheel", new { deltaY = 100, offsetX = 180 });
        await Page.Clock.RunForAsync(500);

        using (Assert.Multiple())
        {
            await Assert.That(await waveform.IntentCountAsync()).IsEqualTo(0);
            await Assert.That(await Page.EvaluateAsync<int>("() => window.paintCount"))
                .IsEqualTo(0);
        }

        await waveform.Canvas.DispatchEventAsync("contextrestored");
        await Page.Clock.RunForAsync(50);
        await waveform.PlacePrimaryCursorAsync(180);
        await Assert.That(await waveform.IntentCountAsync()).IsEqualTo(1);
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
