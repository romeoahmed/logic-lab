using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Pages;

internal sealed class WorkspaceAttachmentNavigation(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Pages/Editor.razor.js";
    internal const string ReadHistoryEntryStateMethod = "readHistoryEntryState";
    internal const string ReplaceHistoryEntryMethod = "replaceHistoryEntry";

    private IJSObjectReference? module;

    public async ValueTask<string?> ReadHistoryEntryStateAsync(
        string localUrl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(localUrl);
        var importedModule = await GetModuleAsync(cancellationToken);
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
        await importedModule.InvokeVoidAsync(
            ReplaceHistoryEntryMethod,
            cancellationToken,
            localUrl,
            attachmentFence);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(
        CancellationToken cancellationToken)
    {
        return module ??= await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            ModulePath);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null)
            {
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
