using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Pages;

internal sealed class WorkspaceAttachmentNavigation(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Pages/Editor.razor.js";
    internal const string ReadHistoryEntryStateMethod = "readHistoryEntryState";
    internal const string ReplaceHistoryEntryMethod = "replaceHistoryEntry";

    private IJSObjectReference? module;
    private bool isDisposed;

    public async ValueTask<string?> ReadHistoryEntryStateAsync(
        string localUrl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(localUrl);
        var importedModule = await GetModuleAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await importedModule.InvokeAsync<string?>(
            ReadHistoryEntryStateMethod,
            cancellationToken,
            localUrl);
    }

    public async ValueTask ReplaceHistoryEntryAsync(
        string localUrl,
        string attachmentFence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(localUrl);
        ArgumentException.ThrowIfNullOrEmpty(attachmentFence);
        var importedModule = await GetModuleAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await importedModule.InvokeVoidAsync(
            ReplaceHistoryEntryMethod,
            cancellationToken,
            localUrl,
            attachmentFence);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (module is not null)
        {
            return module;
        }

        var imported = await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            ModulePath);
        if (isDisposed)
        {
            await DisposeModuleAsync(imported);
            cancellationToken.ThrowIfCancellationRequested();
            throw new ObjectDisposedException(nameof(WorkspaceAttachmentNavigation));
        }

        // Another reentrant call can complete the import while this one is awaiting it.
        if (module is { } existing)
        {
            await DisposeModuleAsync(imported);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return existing;
        }

        module = imported;
        return imported;
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        var retired = module;
        module = null;
        if (retired is not null)
        {
            await DisposeModuleAsync(retired);
        }
    }

    private static async ValueTask DisposeModuleAsync(IJSObjectReference reference)
    {
        try
        {
            await reference.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
