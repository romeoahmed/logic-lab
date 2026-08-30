using System.Security.Cryptography;
using System.Text.Json;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Waveforms;

internal enum WaveformInteractionMode
{
    CommitEnabled,
    LocalOnly,
}

internal sealed class BrowserWaveformAdapter : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Editor/LogicAnalyzer.razor.js";
    private const ulong InteropEnvelopeBytes = 512;
    private readonly IJSObjectReference module;
    private readonly IJSObjectReference handle;
    private readonly BrowserPolicy policy;
    private int isDisposed;

    private BrowserWaveformAdapter(
        IJSObjectReference module,
        IJSObjectReference handle,
        BrowserPolicy policy)
    {
        this.module = module;
        this.handle = handle;
        this.policy = policy;
    }

    public static async Task<BrowserWaveformAdapter> MountAsync<TSink>(
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
            return new BrowserWaveformAdapter(module, handle, policy);
        }
        catch
        {
            await module.DisposeAsync();
            throw;
        }
    }

    public Task ReplaceAsync(
        WaveformSnapshotV1 snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return TransferAsync(
            "snapshot",
            JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                WaveformJsonSerializerContext.Strict.WaveformSnapshotV1),
            cancellationToken);
    }

    public Task ApplyAsync(WaveformPatchV1 patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return TransferAsync(
            "patch",
            JsonSerializer.SerializeToUtf8Bytes(
                patch,
                WaveformJsonSerializerContext.Strict.WaveformPatchV1),
            cancellationToken);
    }

    public ValueTask SetInteractionModeAsync(
        WaveformInteractionMode mode,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var token = mode switch
        {
            WaveformInteractionMode.CommitEnabled => "commitEnabled",
            WaveformInteractionMode.LocalOnly => "localOnly",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
        return handle.InvokeVoidAsync("setInteractionMode", cancellationToken, token);
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
            var rawChunkSize = Math.Max(
                1,
                ((maximumBatch - checked((int)InteropEnvelopeBytes)) / 4) * 3);
            var ordinal = 0;
            for (var offset = 0; offset < candidate.Length; offset += rawChunkSize)
            {
                var length = Math.Min(rawChunkSize, candidate.Length - offset);
                await handle.InvokeVoidAsync(
                    "appendTransfer",
                    cancellationToken,
                    transferId,
                    ordinal,
                    Convert.ToBase64String(candidate, offset, length));
                ordinal++;
            }

            var accepted = await handle.InvokeAsync<bool>(
                "commitTransfer",
                cancellationToken,
                transferId);
            if (!accepted)
            {
                throw new BrowserWaveformContractException(kind);
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
        policy.Limit(BrowserLimitDimension.InteropBatchBytes),
        policy.Limit(BrowserLimitDimension.CandidateTransferBytes),
        policy.Limit(BrowserLimitDimension.CanvasBitmapPixels),
        policy.Limit(BrowserLimitDimension.EffectiveDensityMillionths),
        policy.Limit(BrowserLimitDimension.ZoomMillionthsMinimum),
        policy.Limit(BrowserLimitDimension.ZoomMillionthsMaximum));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed != 0, this);
    }

    private sealed record BrowserPolicyTransferV1(
        string PolicyId,
        string PolicyRevision,
        ulong InteropBatchBytes,
        ulong CandidateTransferBytes,
        ulong CanvasBitmapPixels,
        ulong EffectiveDensityMillionths,
        ulong ZoomMillionthsMinimum,
        ulong ZoomMillionthsMaximum);
}

internal sealed class BrowserWaveformContractException : InvalidOperationException
{
    internal BrowserWaveformContractException(string transferKind)
        : base($"The browser rejected the terminal '{transferKind}' Waveform commit.")
    {
        TransferKind = transferKind;
    }

    public string TransferKind { get; }
}
