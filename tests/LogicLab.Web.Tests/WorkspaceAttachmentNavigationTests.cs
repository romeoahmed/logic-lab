using LogicLab.Web.Components.Pages;
using Microsoft.JSInterop;

namespace LogicLab.Web.Tests;

internal sealed class WorkspaceAttachmentNavigationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Dispose_PendingImport_DisposesLateModuleWithoutInvokingIt(bool cancel)
    {
        var runtime = new DelayedRuntime();
        var module = new RecordingModule();
        using var cancellation = new CancellationTokenSource();
        await using var navigation = new WorkspaceAttachmentNavigation(runtime);
        var read = navigation.ReadHistoryEntryStateAsync("/editor/workspace", cancellation.Token).AsTask();

        await navigation.DisposeAsync();
        if (cancel)
        {
            await cancellation.CancelAsync();
        }
        runtime.Complete(module);

        if (cancel)
        {
            await Assert.That(async () => await read).Throws<OperationCanceledException>();
        }
        else
        {
            await Assert.That(async () => await read).Throws<ObjectDisposedException>();
        }

        await Assert.That(module.DisposalCount).IsEqualTo(1);
        await Assert.That(module.InvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_ImportedModule_IsIdempotentAndRejectsLaterCalls()
    {
        var runtime = new DelayedRuntime();
        var module = new RecordingModule();
        runtime.Complete(module);
        await using var navigation = new WorkspaceAttachmentNavigation(runtime);
        _ = await navigation.ReadHistoryEntryStateAsync("/editor/workspace", CancellationToken.None);

        await navigation.DisposeAsync();
        await navigation.DisposeAsync();

        await Assert.That(async () =>
            await navigation.ReadHistoryEntryStateAsync("/editor/workspace", CancellationToken.None))
            .Throws<ObjectDisposedException>();
        await Assert.That(module.DisposalCount).IsEqualTo(1);
        await Assert.That(module.InvocationCount).IsEqualTo(1);
    }

    private sealed class DelayedRuntime : IJSRuntime
    {
        private readonly TaskCompletionSource<IJSObjectReference> import = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(IJSObjectReference module) => import.SetResult(module);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            (TValue)await import.Task;
    }

    private sealed class RecordingModule : IJSObjectReference
    {
        public int DisposalCount { get; private set; }
        public int InvocationCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            InvocationCount++;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            DisposalCount++;
            return ValueTask.CompletedTask;
        }
    }
}
