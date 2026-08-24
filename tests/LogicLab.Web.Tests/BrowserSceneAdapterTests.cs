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
        var handle = new RecordingJsObjectReference();
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

        await adapter.ReplaceAsync(Snapshot(), CancellationToken.None);
        await adapter.DisposeAsync();

        using var policyPayload = JsonDocument.Parse(
            JsonSerializer.Serialize(
                module.MountArguments?[2],
                JsonOptions));
        var policy = policyPayload.RootElement;

        using (Assert.Multiple())
        {
            await Assert.That(js.Identifiers).IsEquivalentTo(["import"]);
            await Assert.That(module.Identifiers).Contains("mount");
            await Assert.That(handle.Identifiers).Contains("beginTransfer");
            await Assert.That(handle.Identifiers).Contains("appendTransfer");
            await Assert.That(handle.Identifiers).Contains("commitTransfer");
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
        using var measurementJson = JsonDocument.Parse(
            $$"""
            {
              "fontFingerprint": "{{new string('8', 64)}}",
              "measurements": [{
                "key": "measurement-a",
                "advanceWidth": 120,
                "inkLeft": -4,
                "inkTop": -80,
                "inkRight": 116,
                "inkBottom": 20
              }]
            }
            """);
        var handle = new RecordingJsObjectReference(
            measurementRecord: measurementJson.RootElement.Clone());
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
                "measurement-a", "A", "symbol", "center", "en-US", "ltr")],
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(batch.Measurements).Count().IsEqualTo(1);
            await Assert.That(batch.Measurements[0].AdvanceWidth).IsEqualTo(120);
            await Assert.That(handle.Identifiers).Contains("measureText");
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
        JsonElement? measurementRecord = null)
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
                return ValueTask.FromResult((TValue)(object)(measurementRecord
                    ?? throw new InvalidOperationException(
                        "No text measurement record was configured.")));
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
