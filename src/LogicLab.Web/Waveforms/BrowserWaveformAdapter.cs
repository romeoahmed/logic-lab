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
        await BrowserCandidateTransfer.SendAsync(
            handle,
            policy,
            kind,
            candidate,
            static transferKind => new BrowserWaveformContractException(transferKind),
            cancellationToken);
    }

    private static BrowserPolicyTransferV1 PolicyTransfer(BrowserPolicy policy) => new(
        policy.PolicyId,
        policy.PolicyRevision,
        policy.Limit(BrowserLimitDimension.SemanticIntentBytes),
        policy.Limit(BrowserLimitDimension.InteropBatchBytes),
        policy.Limit(BrowserLimitDimension.CandidateTransferBytes),
        policy.Limit(BrowserLimitDimension.CanvasBitmapPixels),
        policy.Limit(BrowserLimitDimension.EffectiveDensityMillionths));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed != 0, this);
    }

    private sealed record BrowserPolicyTransferV1(
        string PolicyId,
        string PolicyRevision,
        ulong SemanticIntentBytes,
        ulong InteropBatchBytes,
        ulong CandidateTransferBytes,
        ulong CanvasBitmapPixels,
        ulong EffectiveDensityMillionths);
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
