using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Tests;

internal sealed class BrowserSceneAdapterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ReplaceAndDispose_UseBoundedTransferAndOneDestroy()
    {
        using var recoveryJson = JsonDocument.Parse(
            """
            {
              "viewports": [{
                "circuitDefinitionId": "definition-a",
                "translateX": 125,
                "translateY": -50,
                "zoom": 2
              }]
            }
            """);
        var handle = new RecordingJsObjectReference(
            recoveryStateRecord: recoveryJson.RootElement.Clone());
        var module = new RecordingJsObjectReference(handle);
        var js = new RecordingJsRuntime(module);
        var initialRecoveryState = new BrowserSceneRecoveryStateV1(
            [new BrowserSceneViewportV1("definition-a", 125, -50, 2)]);
        using var sink = DotNetObjectReference.Create(new Sink());
        await using var adapter = await BrowserSceneAdapter.MountAsync(
            js,
            default(ElementReference),
            "build-a",
            BrowserPolicy.Development,
            sink,
            CancellationToken.None,
            initialRecoveryState);

        await adapter.ReplaceAsync(Snapshot(), CancellationToken.None);
        var recoveryState = await adapter.CaptureRecoveryStateAsync(CancellationToken.None);
        await adapter.DisposeAsync();

        using var policyPayload = JsonDocument.Parse(
            JsonSerializer.Serialize(
                module.MountArguments?[2],
                JsonOptions));
        var policy = policyPayload.RootElement;
        using var recoveryPayload = JsonDocument.Parse(
            JsonSerializer.Serialize(
                module.MountArguments?[4],
                JsonOptions));
        var mountedRecovery = recoveryPayload.RootElement.GetProperty("viewports")[0];

        using (Assert.Multiple())
        {
            await Assert.That(js.Identifiers).IsEquivalentTo(["import"]);
            await Assert.That(module.Identifiers).Contains("mount");
            await Assert.That(handle.Identifiers).Contains("beginTransfer");
            await Assert.That(handle.Identifiers).Contains("appendTransfer");
            await Assert.That(handle.Identifiers).Contains("commitTransfer");
            await Assert.That(handle.Identifiers).Contains("captureRecoveryState");
            await Assert.That(handle.Identifiers.Count(identifier => identifier == "destroy"))
                .IsEqualTo(1);
            await Assert.That(module.IsDisposed).IsTrue();
            await Assert.That(handle.IsDisposed).IsTrue();
            await Assert.That(policy.EnumerateObject().Count()).IsEqualTo(17);
            await Assert.That(policy.GetProperty("semanticIntentBytes").GetUInt64())
                .IsEqualTo(BrowserPolicy.Development.Limit(
                    BrowserLimitDimension.SemanticIntentBytes));
            await Assert.That(policy.GetProperty("interopBatchBytes").GetUInt64())
                .IsEqualTo(BrowserPolicy.Development.Limit(
                    BrowserLimitDimension.InteropBatchBytes));
            await Assert.That(policy.GetProperty("spatialIndexBytes").GetUInt64())
                .IsEqualTo(BrowserPolicy.Development.Limit(
                    BrowserLimitDimension.SpatialIndexBytes));
            await Assert.That(policy.GetProperty("waveformCacheBytes").GetUInt64())
                .IsEqualTo(BrowserPolicy.Development.Limit(
                    BrowserLimitDimension.WaveformCacheBytes));
            await Assert.That(mountedRecovery.GetProperty("circuitDefinitionId").GetString())
                .IsEqualTo("definition-a");
            await Assert.That(mountedRecovery.GetProperty("translateX").GetDouble())
                .IsEqualTo(125);
            await Assert.That(recoveryState.Viewports).Count().IsEqualTo(1);
            await Assert.That(recoveryState.Viewports[0].CircuitDefinitionId)
                .IsEqualTo("definition-a");
            await Assert.That(recoveryState.Viewports[0].TranslateX).IsEqualTo(125);
            await Assert.That(recoveryState.Viewports[0].TranslateY).IsEqualTo(-50);
            await Assert.That(recoveryState.Viewports[0].Zoom).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Replace_BrowserRejectsTerminalCommit_ReportsClosedContractFailure()
    {
        var handle = new RecordingJsObjectReference(commitAccepted: false);
        var module = new RecordingJsObjectReference(handle);
        var js = new RecordingJsRuntime(module);
        using var sink = DotNetObjectReference.Create(new Sink());
        await using var adapter = await BrowserSceneAdapter.MountAsync(
            js,
            default(ElementReference),
            "build-a",
            BrowserPolicy.Development,
            sink,
            CancellationToken.None);

        var exception = await Assert.That(async () =>
                await adapter.ReplaceAsync(Snapshot(), CancellationToken.None))
            .ThrowsExactly<BrowserSceneContractException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.TransferKind).IsEqualTo("replacement");
            await Assert.That(handle.Identifiers).Contains("abortTransfer");
        }
    }

    [Test]
    public async Task MeasureText_JsRecord_IsMaterializedThroughTheStrictJsonBoundary()
    {
        var handle = new RecordingJsObjectReference(
            measurementFactory: CreateMeasurementRecord);
        var module = new RecordingJsObjectReference(handle);
        var js = new RecordingJsRuntime(module);
        using var sink = DotNetObjectReference.Create(new Sink());
        await using var adapter = await BrowserSceneAdapter.MountAsync(
            js,
            default(ElementReference),
            "build-a",
            BrowserPolicy.Development,
            sink,
            CancellationToken.None);

        var batch = await adapter.MeasureTextAsync(
            [new BrowserTextMeasurementRequestV1(
                new string('a', 64), "A", "symbol", "center", "en-US", "ltr")],
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(batch.Measurements).Count().IsEqualTo(1);
            await Assert.That(batch.Measurements[0].AdvanceWidth).IsEqualTo(120);
            await Assert.That(handle.Identifiers).Contains("measureText");
        }
    }

    [Test]
    public async Task MeasureText_LargeRequestSet_UsesBoundedBatchesAndReassemblesOneResult()
    {
        var handle = new RecordingJsObjectReference(
            measurementFactory: CreateMeasurementRecord);
        var module = new RecordingJsObjectReference(handle);
        var js = new RecordingJsRuntime(module);
        using var sink = DotNetObjectReference.Create(new Sink());
        await using var adapter = await BrowserSceneAdapter.MountAsync(
            js,
            default(ElementReference),
            "build-a",
            BrowserPolicy.Development,
            sink,
            CancellationToken.None);
        var requests = Enumerable.Range(0, 200)
            .Select(index => new BrowserTextMeasurementRequestV1(
                index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture),
                new string('A', 200),
                "symbol",
                "center",
                "en-US",
                "ltr"))
            .ToArray();

        var batch = await adapter.MeasureTextAsync(requests, CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(batch.Measurements).Count().IsEqualTo(requests.Length);
            await Assert.That(batch.Measurements.Select(measurement => measurement.Key))
                .IsEquivalentTo(requests.Select(request => request.Key));
            await Assert.That(handle.Identifiers.Count(identifier => identifier == "measureText"))
                .IsGreaterThan(1);
        }
    }

    private static SceneSnapshotV1 Snapshot() => new(
        "build-a",
        1,
        1,
        "definition-a",
        "en-US",
        "leftToRight",
        "projection-a",
        new SceneRect(0, 0, 100, 100),
        100,
        1,
        new string('9', 64),
        [new SceneItemV1(
            new SceneSourceRefV1("definition-a", "componentInstance", "a"),
            0,
            new SceneRect(0, 0, 10, 10),
            new ScenePoint(0, 0),
            [],
            [],
            new SceneComponentInteractionV1(new SceneComponentPlacementV1(
                new SceneGridPointV1(0, 0),
                0,
                false)))],
        []);

    private static JsonElement CreateMeasurementRecord(object?[]? arguments)
    {
        var requests = arguments?[0]
            as IReadOnlyList<BrowserTextMeasurementRequestV1>
            ?? throw new InvalidOperationException("No measurement requests were provided.");
        var assetFingerprint = new string('8', 64);
        var measurements = requests.Select(request => new BrowserTextMeasurementV1(
            request.Key,
            120,
            -4,
            -80,
            116,
            20)).ToArray();
        var canonical = string.Join('\n', measurements
            .OrderBy(measurement => measurement.Key, StringComparer.Ordinal)
            .Select(measurement => $"{measurement.Key}:{measurement.AdvanceWidth}:"
                + $"{measurement.InkLeft}:{measurement.InkTop}:{measurement.InkRight}:"
                + $"{measurement.InkBottom}"));
        var fontFingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"logiclab-browser-font-v1\nAtkinson Hyperlegible Next\n"
                + $"{assetFingerprint}\n{canonical}")));
        return JsonSerializer.SerializeToElement(
            new
            {
                FontFamily = "Atkinson Hyperlegible Next",
                AssetFingerprint = assetFingerprint,
                FontFingerprint = fontFingerprint,
                Measurements = measurements,
            },
            JsonOptions);
    }

    private sealed class Sink;

    private sealed class RecordingJsRuntime(IJSObjectReference module) : IJSRuntime
    {
        public List<string> Identifiers { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Identifiers.Add(identifier);
            return ValueTask.FromResult((TValue)module);
        }
    }

    private sealed class RecordingJsObjectReference(
        IJSObjectReference? mountedHandle = null,
        bool commitAccepted = true,
        JsonElement? measurementRecord = null,
        JsonElement? recoveryStateRecord = null,
        Func<object?[]?, JsonElement>? measurementFactory = null)
        : IJSObjectReference
    {
        public List<string> Identifiers { get; } = [];

        public bool IsDisposed { get; private set; }

        public object?[]? MountArguments { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Identifiers.Add(identifier);
            if (identifier == "mount")
            {
                MountArguments = args;
                return ValueTask.FromResult((TValue)(mountedHandle
                    ?? throw new InvalidOperationException("No mounted handle was configured.")));
            }

            if (identifier == "commitTransfer" && typeof(TValue) == typeof(bool))
            {
                return ValueTask.FromResult((TValue)(object)commitAccepted);
            }

            if (identifier == "measureText" && typeof(TValue) == typeof(JsonElement))
            {
                return ValueTask.FromResult((TValue)(object)(measurementFactory?.Invoke(args)
                    ?? measurementRecord
                    ?? throw new InvalidOperationException(
                        "No text measurement record was configured.")));
            }

            if (identifier == "captureRecoveryState" && typeof(TValue) == typeof(JsonElement))
            {
                return ValueTask.FromResult((TValue)(object)(recoveryStateRecord
                    ?? throw new InvalidOperationException(
                        "No recovery state record was configured.")));
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
