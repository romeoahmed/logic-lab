using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Scene;

internal sealed class BrowserSceneAdapter : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Editor/CircuitSceneHost.razor.js";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IJSObjectReference module;
    private readonly IJSObjectReference handle;
    private readonly BrowserPolicy policy;
    private int isDisposed;

    private BrowserSceneAdapter(
        IJSObjectReference module,
        IJSObjectReference handle,
        BrowserPolicy policy)
    {
        this.module = module;
        this.handle = handle;
        this.policy = policy;
    }

    public static async Task<BrowserSceneAdapter> MountAsync<TSink>(
        IJSRuntime js,
        ElementReference host,
        string buildFingerprint,
        BrowserPolicy policy,
        DotNetObjectReference<TSink> sink,
        CancellationToken cancellationToken)
        where TSink : class
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(sink);
        var module = await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            ModulePath);
        try
        {
            var handle = await module.InvokeAsync<IJSObjectReference>(
                "mount",
                cancellationToken,
                host,
                buildFingerprint,
                PolicyTransfer(policy),
                sink);
            return new BrowserSceneAdapter(module, handle, policy);
        }
        catch
        {
            await module.DisposeAsync();
            throw;
        }
    }

    public ValueTask<BrowserTextMeasurementBatchV1> MeasureTextAsync(
        IReadOnlyList<BrowserTextMeasurementRequestV1> requests,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(requests);
        return handle.InvokeAsync<BrowserTextMeasurementBatchV1>(
            "measureText",
            cancellationToken,
            requests);
    }

    public Task ReplaceAsync(
        ISceneReplacementV1 replacement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return TransferAsync(
            "replacement",
            JsonSerializer.SerializeToUtf8Bytes(replacement, replacement.GetType(), JsonOptions),
            cancellationToken);
    }

    public Task ApplyAsync(ScenePatchV1 patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return TransferAsync(
            "patch",
            JsonSerializer.SerializeToUtf8Bytes(patch, JsonOptions),
            cancellationToken);
    }

    public ValueTask SetConnectedAsync(bool isConnected, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return handle.InvokeVoidAsync("setConnected", cancellationToken, isConnected);
    }

    public ValueTask FocusSourceAsync(string sourceKey, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        return handle.InvokeVoidAsync("focusSource", cancellationToken, sourceKey);
    }

    public ValueTask SetSelectionAsync(
        IReadOnlyList<SceneSourceRefV1> sources,
        string selectionMode,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionMode);
        return handle.InvokeVoidAsync(
            "setSelection",
            cancellationToken,
            sources,
            selectionMode);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await handle.InvokeVoidAsync("destroy");
        }
        catch (JSDisconnectedException)
        {
        }
        finally
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }

            try
            {
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task TransferAsync(
        string kind,
        byte[] candidate,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var maximumCandidate = policy.Limit(BrowserLimitDimension.CandidateTransferBytes);
        if ((ulong)candidate.Length > maximumCandidate)
        {
            throw new BrowserPolicyException(
                policy,
                BrowserLimitDimension.CandidateTransferBytes,
                (ulong)candidate.Length);
        }

        var transferId = Guid.CreateVersion7().ToString("N");
        var digest = Convert.ToHexStringLower(SHA256.HashData(candidate));
        await handle.InvokeVoidAsync(
            "beginTransfer",
            cancellationToken,
            transferId,
            kind,
            candidate.Length,
            digest);
        try
        {
            var maximumBatch = checked((int)Math.Min(
                policy.Limit(BrowserLimitDimension.InteropBatchBytes),
                int.MaxValue));
            var rawChunkSize = Math.Max(1, ((maximumBatch - 512) / 4) * 3);
            var ordinal = 0;
            for (var offset = 0; offset < candidate.Length; offset += rawChunkSize)
            {
                var length = Math.Min(rawChunkSize, candidate.Length - offset);
                var chunk = Convert.ToBase64String(candidate, offset, length);
                await handle.InvokeVoidAsync(
                    "appendTransfer",
                    cancellationToken,
                    transferId,
                    ordinal,
                    chunk);
                ordinal++;
            }

            var accepted = await handle.InvokeAsync<bool>(
                "commitTransfer",
                cancellationToken,
                transferId);
            if (!accepted)
            {
                throw new BrowserSceneContractException(kind);
            }
        }
        catch
        {
            try
            {
                await handle.InvokeVoidAsync("abortTransfer", transferId);
            }
            catch (Exception exception) when (exception is JSException
                or JSDisconnectedException
                or ObjectDisposedException)
            {
            }

            throw;
        }
    }

    private static BrowserPolicyTransferV1 PolicyTransfer(BrowserPolicy policy) => new(
        policy.PolicyId,
        policy.PolicyRevision,
        policy.Limit(BrowserLimitDimension.SemanticIntentBytes),
        policy.Limit(BrowserLimitDimension.SceneSnapshotRecordCount),
        policy.Limit(BrowserLimitDimension.ScenePatchRecordCount),
        policy.Limit(BrowserLimitDimension.InteropBatchBytes),
        policy.Limit(BrowserLimitDimension.CandidateTransferBytes),
        policy.Limit(BrowserLimitDimension.CanvasBitmapPixels),
        policy.Limit(BrowserLimitDimension.CanvasBitmapBytes),
        policy.Limit(BrowserLimitDimension.EffectiveDensityMillionths),
        policy.Limit(BrowserLimitDimension.ZoomMillionthsMinimum),
        policy.Limit(BrowserLimitDimension.ZoomMillionthsMaximum),
        policy.Limit(BrowserLimitDimension.SemanticTreePageItems),
        policy.Limit(BrowserLimitDimension.DisplayListBytes),
        policy.Limit(BrowserLimitDimension.SpatialIndexBytes),
        policy.Limit(BrowserLimitDimension.SceneCacheBytes),
        policy.Limit(BrowserLimitDimension.WaveformCacheBytes));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed != 0, this);
    }

    private sealed record BrowserPolicyTransferV1(
        string PolicyId,
        string PolicyRevision,
        ulong SemanticIntentBytes,
        ulong SceneSnapshotRecordCount,
        ulong ScenePatchRecordCount,
        ulong InteropBatchBytes,
        ulong CandidateTransferBytes,
        ulong CanvasBitmapPixels,
        ulong CanvasBitmapBytes,
        ulong EffectiveDensityMillionths,
        ulong ZoomMillionthsMinimum,
        ulong ZoomMillionthsMaximum,
        ulong SemanticTreePageItems,
        ulong DisplayListBytes,
        ulong SpatialIndexBytes,
        ulong SceneCacheBytes,
        ulong WaveformCacheBytes);
}

public sealed class BrowserSceneContractException : InvalidOperationException
{
    internal BrowserSceneContractException(string transferKind)
        : base($"The browser rejected the terminal '{transferKind}' Scene commit.")
    {
        TransferKind = transferKind;
    }

    public string TransferKind { get; }
}

public sealed class BrowserPolicyException : InvalidOperationException
{
    internal BrowserPolicyException(
        BrowserPolicy policy,
        BrowserLimitDimension dimension,
        ulong observed)
        : base($"Browser Policy '{policy.PolicyId}/{policy.PolicyRevision}' rejected "
            + $"'{dimension}' at observed value '{observed}'.")
    {
        PolicyId = policy.PolicyId;
        PolicyRevision = policy.PolicyRevision;
        Dimension = dimension;
        Observed = observed;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public BrowserLimitDimension Dimension { get; }

    public ulong Observed { get; }
}
