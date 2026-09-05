using System.Security.Cryptography;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using LogicLab.Web.Scene;
using Microsoft.JSInterop;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Web.Tests;

internal sealed class BrowserCandidateTransferTests
{
    [Test, FsCheckProperty(MaxTest = 50)]
    public async Task<Property> SendAsync_GeneratedPayload_PreservesTheTransferContract(
        NonEmptyArray<byte> generatedPayload)
    {
        var payload = generatedPayload.Get;
        var policy = TransferPolicy();
        var handle = new RecordingTransferHandle();

        await BrowserCandidateTransfer.SendAsync(
            handle,
            policy,
            "candidate",
            payload,
            kind => new InvalidOperationException(kind),
            CancellationToken.None);

        var begin = handle.Calls.Single(call => call.Identifier == "beginTransfer");
        var chunks = handle.Calls
            .Where(call => call.Identifier == "appendTransfer")
            .ToArray();
        var reconstructed = chunks
            .SelectMany(call => Convert.FromBase64String((string)call.Arguments[2]!))
            .ToArray();
        var transferId = (string)begin.Arguments[0]!;

        return (begin.Arguments[1] as string == "candidate"
                && (int)begin.Arguments[2]! == payload.Length
                && begin.Arguments[3] as string
                    == Convert.ToHexStringLower(SHA256.HashData(payload)))
            .Label("beginTransfer describes the generated candidate")
            .And(chunks.Select(call => (int)call.Arguments[1]!)
                .SequenceEqual(Enumerable.Range(0, chunks.Length))
                .Label("append ordinals are contiguous in invocation order"))
            .And(chunks.All(call =>
                    checked((ulong)Encoding.UTF8.GetByteCount(
                            (string)call.Arguments[2]!))
                        + BrowserPolicy.InteropEnvelopeBytes
                        <= policy.Limit(BrowserLimitDimension.InteropBatchBytes))
                .Label("every chunk fits the interop budget"))
            .And(reconstructed.SequenceEqual(payload)
                .Label("ordered Base64 chunks reconstruct the candidate exactly"))
            .And(handle.Calls.Select(call => call.Identifier).SequenceEqual(
                    Enumerable.Repeat("appendTransfer", chunks.Length)
                        .Prepend("beginTransfer")
                        .Append("commitTransfer"))
                .Label("all chunks follow begin and precede the sole commit"))
            .And(handle.Calls.All(call => call.Arguments[0] as string == transferId)
                .Label("begin, append, and commit share one transfer identity"))
            .Collect($"payload-bytes={payload.Length}");
    }

    [Test]
    public async Task SendAsync_CommitRejected_AbortsAndPreservesTheMappedFailure()
    {
        var handle = new RecordingTransferHandle(commitAccepted: false);

        var exception = await Assert.That(() => BrowserCandidateTransfer.SendAsync(
                handle,
                TransferPolicy(),
                "candidate",
                [1, 2, 3, 4],
                kind => new BrowserSceneContractException(kind),
                CancellationToken.None))
            .ThrowsExactly<BrowserSceneContractException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.TransferKind).IsEqualTo("candidate");
            await Assert.That(handle.Calls.Count(call => call.Identifier == "abortTransfer"))
                .IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SendAsync_CancelledDuringAppend_AbortsWithoutMaskingCancellation(bool abortFails)
    {
        using var cancellation = new CancellationTokenSource();
        var handle = new RecordingTransferHandle(beforeInvoke: identifier =>
        {
            if (identifier == "appendTransfer")
            {
                cancellation.Cancel();
            }
            else if (identifier == "abortTransfer" && abortFails)
            {
                throw new JSDisconnectedException("The browser disconnected during cleanup.");
            }
        });

        var exception = await Assert.That(() => BrowserCandidateTransfer.SendAsync(
                handle,
                TransferPolicy(),
                "candidate",
                [1, 2, 3, 4],
                kind => new InvalidOperationException(kind),
                cancellation.Token))
            .ThrowsExactly<OperationCanceledException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(handle.Calls.Select(call => call.Identifier))
                .IsEquivalentTo(["beginTransfer", "appendTransfer", "abortTransfer"], CollectionOrdering.Matching);
            await Assert.That(handle.Calls[^1].CancellationToken.CanBeCanceled).IsFalse();
            await Assert.That(handle.Calls[^1].Arguments[0]).IsEqualTo(handle.Calls[0].Arguments[0]);
        }
    }

    [Test]
    public async Task SendAsync_CandidateAbovePolicy_RejectsBeforeInterop()
    {
        var handle = new RecordingTransferHandle();

        var exception = await Assert.That(() => BrowserCandidateTransfer.SendAsync(
                handle,
                TransferPolicy(candidateTransferBytes: 3),
                "candidate",
                [1, 2, 3, 4],
                kind => new InvalidOperationException(kind),
                CancellationToken.None))
            .ThrowsExactly<BrowserPolicyException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.Dimension)
                .IsEqualTo(BrowserLimitDimension.CandidateTransferBytes);
            await Assert.That(handle.Calls).IsEmpty();
        }
    }

    private static BrowserPolicy TransferPolicy(
        ulong? candidateTransferBytes = null) => new(
        "logiclab-browser",
        "transfer-test",
        [.. BrowserPolicy.Default.Limits.Select(limit => limit.Dimension switch
        {
            BrowserLimitDimension.InteropBatchBytes => limit with
            {
                Value = BrowserPolicy.MinimumInteropBatchBytes,
            },
            BrowserLimitDimension.CandidateTransferBytes
                when candidateTransferBytes is { } candidateLimit => limit with
                {
                    Value = candidateLimit,
                },
            _ => limit,
        })]);

    private sealed record JsCall(string Identifier, object?[] Arguments, CancellationToken CancellationToken);

    private sealed class RecordingTransferHandle(
        bool commitAccepted = true,
        Action<string>? beforeInvoke = null)
        : IJSObjectReference
    {
        public List<JsCall> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Calls.Add(new JsCall(identifier, args ?? [], cancellationToken));
            beforeInvoke?.Invoke(identifier);
            cancellationToken.ThrowIfCancellationRequested();
            return identifier == "commitTransfer" && typeof(TValue) == typeof(bool)
                ? ValueTask.FromResult((TValue)(object)commitAccepted)
                : ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
