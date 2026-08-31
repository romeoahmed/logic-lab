using System.Security.Cryptography;
using Microsoft.JSInterop;

namespace LogicLab.Web.Scene;

internal static class BrowserCandidateTransfer
{
    public static async Task SendAsync(
        IJSObjectReference handle,
        BrowserPolicy policy,
        string kind,
        byte[] candidate,
        Func<string, Exception> createCommitRejection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfZero(candidate.Length);
        ArgumentNullException.ThrowIfNull(createCommitRejection);

        var maximumCandidate = policy.Limit(BrowserLimitDimension.CandidateTransferBytes);
        if ((ulong)candidate.Length > maximumCandidate)
        {
            throw new BrowserPolicyException(
                policy,
                BrowserLimitDimension.CandidateTransferBytes,
                (ulong)candidate.Length);
        }

        var maximumBatch = policy.Limit(BrowserLimitDimension.InteropBatchBytes);
        var encodedPayloadBudget = maximumBatch - BrowserPolicy.InteropEnvelopeBytes;
        var rawChunkSize = checked((int)Math.Min(
            encodedPayloadBudget / 4 * 3,
            int.MaxValue));
        var transferId = Guid.CreateVersion7().ToString("N");
        var digest = Convert.ToHexStringLower(SHA256.HashData(candidate));
        try
        {
            await handle.InvokeVoidAsync(
                "beginTransfer",
                cancellationToken,
                transferId,
                kind,
                candidate.Length,
                digest);
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
                throw createCommitRejection(kind);
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
                or InvalidOperationException
                or ObjectDisposedException)
            {
            }

            throw;
        }
    }
}
