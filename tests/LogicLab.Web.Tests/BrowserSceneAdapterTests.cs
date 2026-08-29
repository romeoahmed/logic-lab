using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Tests;

internal sealed class BrowserSceneAdapterTests
{
    private const int InteropEnvelopeBytes = 512;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Mount_PolicyAndRecovery_CrossTheTypedBoundary()
    {
        var handle = new RecordingJsObjectReference();
        var module = new RecordingJsObjectReference(handle);
        var recovery = new BrowserSceneRecoveryStateV1(
            [new BrowserSceneViewportV1("definition-a", 125, -50, 2)]);
        using var sink = DotNetObjectReference.Create(new Sink());
        await using var adapter = await BrowserSceneAdapter.MountAsync(
            new RecordingJsRuntime(module),
            default(ElementReference),
            "build-a",
            BrowserPolicy.Default,
            sink,
            CancellationToken.None,
            recovery);

        var mount = module.Calls.Single(call => call.Identifier == "mount");
        var policyJson = JsonSerializer.Serialize(mount.Arguments[2], WebJson);
        var policy = JsonSerializer.Deserialize<BrowserPolicyPayload>(policyJson, WebJson);
        var mountedRecovery = (BrowserSceneRecoveryStateV1)mount.Arguments[4]!;

        using (Assert.Multiple())
        {
            await Assert.That(policy).IsEqualTo(BrowserPolicyPayload.From(
                BrowserPolicy.Default));
            await Assert.That(mountedRecovery.Viewports).IsEquivalentTo(recovery.Viewports);
        }
    }

    [Test]
    public async Task Replace_LargeSnapshot_UsesBoundedOrderedChunksThatReconstructCandidate()
    {
        var handle = new RecordingJsObjectReference();
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        await using var adapter = mounted.Adapter;
        var snapshot = Snapshot(itemCount: 200);
        var expected = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            SceneJsonSerializerContext.Strict.SceneSnapshotV1);

        await adapter.ReplaceAsync(snapshot, CancellationToken.None);

        var begin = handle.Calls.Single(call => call.Identifier == "beginTransfer");
        var chunks = handle.Calls
            .Where(call => call.Identifier == "appendTransfer")
            .OrderBy(call => (int)call.Arguments[1]!)
            .ToArray();
        var reconstructed = chunks
            .SelectMany(call => Convert.FromBase64String((string)call.Arguments[2]!))
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(chunks).Count().IsGreaterThan(1);
            await Assert.That(chunks.Select((call, ordinal) =>
                    (int)call.Arguments[1]! == ordinal).All(value => value))
                .IsTrue();
            await Assert.That(chunks.All(call =>
                    Encoding.UTF8.GetByteCount((string)call.Arguments[2]!)
                        + InteropEnvelopeBytes
                    <= (int)BrowserPolicy.Default.Limit(
                        BrowserLimitDimension.InteropBatchBytes)))
                .IsTrue();
            await Assert.That(reconstructed.SequenceEqual(expected)).IsTrue();
            await Assert.That(begin.Arguments[1]).IsEqualTo("replacement");
            await Assert.That(begin.Arguments[2]).IsEqualTo(expected.Length);
            await Assert.That(begin.Arguments[3]).IsEqualTo(
                Convert.ToHexStringLower(SHA256.HashData(expected)));
            await Assert.That(handle.Calls.Count(call => call.Identifier == "commitTransfer"))
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task Replace_TerminalCommitRejected_AbortsAndReportsTransferKind()
    {
        var handle = new RecordingJsObjectReference(commitAccepted: false);
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        await using var adapter = mounted.Adapter;

        var exception = await Assert.That(async () =>
                await adapter.ReplaceAsync(Snapshot(), CancellationToken.None))
            .ThrowsExactly<BrowserSceneContractException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.TransferKind).IsEqualTo("replacement");
            await Assert.That(handle.Calls.Count(call => call.Identifier == "abortTransfer"))
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task MeasureText_LargeRequestSet_BatchesWithinPolicyAndReassemblesResults()
    {
        var handle = new RecordingJsObjectReference(
            measurementFactory: CreateMeasurementRecord);
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        await using var adapter = mounted.Adapter;
        var requests = Enumerable.Range(0, 200)
            .Select(index => new BrowserTextMeasurementRequestV1(
                index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture),
                new string('A', 200),
                "symbol",
                "center",
                "en-US",
                "ltr"))
            .ToArray();

        var result = await adapter.MeasureTextAsync(requests, CancellationToken.None);
        var batches = handle.Calls.Where(call => call.Identifier == "measureText").ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(batches).Count().IsGreaterThan(1);
            await Assert.That(batches.All(call =>
                    Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(call.Arguments[0], WebJson))
                        + InteropEnvelopeBytes
                    <= (int)BrowserPolicy.Default.Limit(
                        BrowserLimitDimension.InteropBatchBytes)))
                .IsTrue();
            await Assert.That(result.Measurements.Select(measurement => measurement.Key))
                .IsEquivalentTo(requests.Select(request => request.Key));
        }
    }

    [Test]
    public async Task Dispose_RepeatedCall_DestroysHandleOnceAndDisposesBothReferences()
    {
        var handle = new RecordingJsObjectReference();
        var module = new RecordingJsObjectReference(handle);
        using var sink = DotNetObjectReference.Create(new Sink());
        var adapter = await BrowserSceneAdapter.MountAsync(
            new RecordingJsRuntime(module),
            default(ElementReference),
            "build-a",
            BrowserPolicy.Default,
            sink,
            CancellationToken.None);

        await adapter.DisposeAsync();
        await adapter.DisposeAsync();

        using (Assert.Multiple())
        {
            await Assert.That(handle.Calls.Count(call => call.Identifier == "destroy"))
                .IsEqualTo(1);
            await Assert.That(handle.IsDisposed).IsTrue();
            await Assert.That(module.IsDisposed).IsTrue();
        }
    }

    [Test]
    public async Task CaptureRecoveryState_ValidBrowserRecord_ReturnsImmutableContract()
    {
        using var recoveryJson = JsonDocument.Parse(
            """
            {"viewports":[{
              "circuitDefinitionId":"definition-a",
              "translateX":125,
              "translateY":-50,
              "zoom":2
            }]}
            """);
        var handle = new RecordingJsObjectReference(
            recoveryStateRecord: recoveryJson.RootElement.Clone());
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        await using var adapter = mounted.Adapter;

        var recovery = await adapter.CaptureRecoveryStateAsync(CancellationToken.None);
        var viewport = recovery.Viewports.Single();

        using (Assert.Multiple())
        {
            await Assert.That(viewport.CircuitDefinitionId).IsEqualTo("definition-a");
            await Assert.That(viewport.TranslateX).IsEqualTo(125);
            await Assert.That(viewport.TranslateY).IsEqualTo(-50);
            await Assert.That(viewport.Zoom).IsEqualTo(2);
        }
    }

    private static async Task<MountedAdapter> MountAsync(
        RecordingJsObjectReference handle)
    {
        var module = new RecordingJsObjectReference(handle);
        var sink = DotNetObjectReference.Create(new Sink());
        try
        {
            var adapter = await BrowserSceneAdapter.MountAsync(
                new RecordingJsRuntime(module),
                default(ElementReference),
                "build-a",
                BrowserPolicy.Default,
                sink,
                CancellationToken.None);
            return new MountedAdapter(adapter, sink);
        }
        catch
        {
            sink.Dispose();
            throw;
        }
    }

    private static SceneSnapshotV1 Snapshot(int itemCount = 1) => new(
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
        [.. Enumerable.Range(0, itemCount).Select(index => new SceneItemV1(
            new SceneSourceRefV1(
                "definition-a",
                "componentInstance",
                $"component-{index:D4}"),
            index,
            new SceneRect(0, 0, 10, 10),
            new ScenePoint(0, 0),
            [],
            [],
            new SceneComponentInteractionV1(new SceneComponentPlacementV1(
                new SceneGridPointV1(index, 0),
                0,
                false))))],
        []);

    private static JsonElement CreateMeasurementRecord(object?[] arguments)
    {
        var requests = arguments[0] as IReadOnlyList<BrowserTextMeasurementRequestV1>
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
            WebJson);
    }

    private sealed class Sink;

    private sealed record MountedAdapter(
        BrowserSceneAdapter Adapter,
        DotNetObjectReference<Sink> Sink);

    private sealed record JsCall(string Identifier, object?[] Arguments);

    private sealed class RecordingJsRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult((TValue)module);
        }
    }

    private sealed class RecordingJsObjectReference(
        IJSObjectReference? mountedHandle = null,
        bool commitAccepted = true,
        JsonElement? recoveryStateRecord = null,
        Func<object?[], JsonElement>? measurementFactory = null)
        : IJSObjectReference
    {
        public List<JsCall> Calls { get; } = [];

        public bool IsDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var arguments = args ?? [];
            Calls.Add(new JsCall(identifier, arguments));
            return identifier switch
            {
                "mount" => ValueTask.FromResult((TValue)(mountedHandle
                    ?? throw new InvalidOperationException("No mounted handle was configured."))),
                "commitTransfer" when typeof(TValue) == typeof(bool) =>
                    ValueTask.FromResult((TValue)(object)commitAccepted),
                "measureText" when typeof(TValue) == typeof(JsonElement) =>
                    ValueTask.FromResult((TValue)(object)(measurementFactory?.Invoke(arguments)
                        ?? throw new InvalidOperationException(
                            "No text measurement record was configured."))),
                "captureRecoveryState" when typeof(TValue) == typeof(JsonElement) =>
                    ValueTask.FromResult((TValue)(object)(recoveryStateRecord
                        ?? throw new InvalidOperationException(
                            "No recovery state record was configured."))),
                _ => ValueTask.FromResult(default(TValue)!),
            };
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record BrowserPolicyPayload(
        string PolicyId,
        string PolicyRevision,
        ulong SemanticIntentBytes,
        ulong SceneSnapshotRecordCount,
        ulong ScenePatchRecordCount,
        ulong InteropBatchBytes,
        ulong CandidateTransferBytes,
        ulong CanvasBitmapPixels,
        ulong EffectiveDensityMillionths,
        ulong ZoomMillionthsMinimum,
        ulong ZoomMillionthsMaximum,
        ulong DisplayListBytes,
        ulong SpatialIndexBytes,
        ulong SceneCacheBytes)
    {
        public static BrowserPolicyPayload From(BrowserPolicy policy) => new(
            policy.PolicyId,
            policy.PolicyRevision,
            policy.Limit(BrowserLimitDimension.SemanticIntentBytes),
            policy.Limit(BrowserLimitDimension.SceneSnapshotRecordCount),
            policy.Limit(BrowserLimitDimension.ScenePatchRecordCount),
            policy.Limit(BrowserLimitDimension.InteropBatchBytes),
            policy.Limit(BrowserLimitDimension.CandidateTransferBytes),
            policy.Limit(BrowserLimitDimension.CanvasBitmapPixels),
            policy.Limit(BrowserLimitDimension.EffectiveDensityMillionths),
            policy.Limit(BrowserLimitDimension.ZoomMillionthsMinimum),
            policy.Limit(BrowserLimitDimension.ZoomMillionthsMaximum),
            policy.Limit(BrowserLimitDimension.DisplayListBytes),
            policy.Limit(BrowserLimitDimension.SpatialIndexBytes),
            policy.Limit(BrowserLimitDimension.SceneCacheBytes));
    }
}
