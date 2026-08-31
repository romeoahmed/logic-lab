using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class BrowserWaveformAdapterTests
{
    [Test]
    public async Task Replace_LargeSnapshot_UsesBoundedOrderedAtomicTransfer()
    {
        var handle = new RecordingJsObjectReference();
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        await using var adapter = mounted.Adapter;
        var snapshot = Snapshot(rowCount: 220);
        var expected = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            WaveformJsonSerializerContext.Strict.WaveformSnapshotV1);

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
            await Assert.That(chunks.Select(call => (int)call.Arguments[1]!))
                .IsEquivalentTo(Enumerable.Range(0, chunks.Length), CollectionOrdering.Matching);
            await Assert.That(chunks.Max(call =>
                    Encoding.UTF8.GetByteCount((string)call.Arguments[2]!)
                        + (int)BrowserPolicy.InteropEnvelopeBytes))
                .IsLessThanOrEqualTo((int)BrowserPolicy.Default.Limit(
                    BrowserLimitDimension.InteropBatchBytes));
            await Assert.That(reconstructed)
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(begin.Arguments[1]).IsEqualTo("snapshot");
            await Assert.That(begin.Arguments[2]).IsEqualTo(expected.Length);
            await Assert.That(begin.Arguments[3]).IsEqualTo(
                Convert.ToHexStringLower(SHA256.HashData(expected)));
        }
    }

    [Test]
    public async Task InteractionMode_AndRepeatedDispose_AreForwardedAndReleasedOnce()
    {
        var handle = new RecordingJsObjectReference();
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        var adapter = mounted.Adapter;

        await adapter.SetInteractionModeAsync(
            WaveformInteractionMode.LocalOnly,
            CancellationToken.None);
        await adapter.DisposeAsync();
        await adapter.DisposeAsync();

        using (Assert.Multiple())
        {
            await Assert.That(handle.Calls.Single(call =>
                    call.Identifier == "setInteractionMode").Arguments[0])
                .IsEqualTo("localOnly");
            await Assert.That(handle.Calls.Count(call => call.Identifier == "destroy"))
                .IsEqualTo(1);
            await Assert.That(handle.IsDisposed).IsTrue();
            await Assert.That(mounted.Module.IsDisposed).IsTrue();
        }
    }

    [Test]
    public async Task Replace_BrowserRejectsTerminalCommit_AbortsWithoutPublishing()
    {
        var handle = new RecordingJsObjectReference(commitAccepted: false);
        var mounted = await MountAsync(handle);
        using var sink = mounted.Sink;
        await using var adapter = mounted.Adapter;

        var exception = await Assert.That(() => adapter.ReplaceAsync(
                Snapshot(rowCount: 1),
                CancellationToken.None))
            .ThrowsExactly<BrowserWaveformContractException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.TransferKind).IsEqualTo("snapshot");
            await Assert.That(handle.Calls.Count(call =>
                    call.Identifier == "abortTransfer"))
                .IsEqualTo(1);
        }
    }

    private static async Task<MountedAdapter> MountAsync(
        RecordingJsObjectReference handle,
        BrowserPolicy? policy = null)
    {
        var module = new RecordingJsObjectReference(handle);
        var sink = DotNetObjectReference.Create(new Sink());
        try
        {
            var adapter = await BrowserWaveformAdapter.MountAsync(
                new RecordingJsRuntime(module),
                default(ElementReference),
                "build-a",
                policy ?? BrowserPolicy.Default,
                sink,
                CancellationToken.None);
            return new MountedAdapter(adapter, module, sink);
        }
        catch
        {
            sink.Dispose();
            throw;
        }
    }

    private static WaveformSnapshotV1 Snapshot(int rowCount)
    {
        var rows = Enumerable.Range(0, rowCount)
            .Select(index => Row(index))
            .ToArray();
        return new WaveformSnapshotV1(
            "build-a",
            1,
            3,
            "session-a",
            4,
            "artifact-a",
            rows,
            new WaveformViewStateV1(
                new WaveformTimeRangeV1("0", "10"),
                primaryCursor: null,
                secondaryCursor: null),
            new WaveformTransitionsViewV1(
                [.. rows.Select(row => new WaveformTransitionSegmentV1(
                    row.ProbeId,
                    new WaveformTimeRangeV1("0", "10"),
                    row.CurrentValue!,
                    transitionAtStart: false))]));
    }

    private static WaveformRowV1 Row(int index) => new(
        $"probe-{index:D4}",
        new SceneElaboratedNetRefV1(
            new SceneSourceRefV1("main", "net", $"net-{index:D4}"),
            new SceneHierarchyPathV1("main", [])),
        width: 32,
        displayOrdinal: index,
        shortLabel: new string('S', 140),
        radix: "hex",
        appearanceOrdinal: checked((uint)index),
        pattern: "solid",
        binding: "resolved",
        bindingReason: null,
        sceneNavigation: "available",
        navigationReason: null,
        currentValue: new WaveformLogicVectorV1(
            32,
            "logic4-2bit-v1",
            Convert.ToBase64String(new byte[8])));

    private sealed class Sink;

    private sealed record MountedAdapter(
        BrowserWaveformAdapter Adapter,
        RecordingJsObjectReference Module,
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
        bool commitAccepted = true) : IJSObjectReference
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
                _ => ValueTask.FromResult(default(TValue)!),
            };
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
