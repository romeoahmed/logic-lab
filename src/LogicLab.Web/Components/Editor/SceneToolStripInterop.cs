using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Editor;

internal sealed class SceneToolStripInterop(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Editor/SceneToolStrip.razor.js";
    private const string MountMethod = "mount";
    private const string DestroyMethod = "destroy";

    private IJSObjectReference? module;
    private IJSObjectReference? handle;

    public async ValueTask MountAsync(ElementReference toolbar)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        handle ??= await module.InvokeAsync<IJSObjectReference>(MountMethod, toolbar);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (handle is not null)
            {
                await handle.InvokeVoidAsync(DestroyMethod);
            }
        }
        catch (Exception exception) when (IsExpectedTeardownException(exception))
        {
        }
        finally
        {
            await DisposeReferenceAsync(handle);
            await DisposeReferenceAsync(module);
            handle = null;
            module = null;
        }
    }

    private static async ValueTask DisposeReferenceAsync(IJSObjectReference? reference)
    {
        if (reference is null)
        {
            return;
        }

        try
        {
            await reference.DisposeAsync();
        }
        catch (Exception exception) when (IsExpectedTeardownException(exception))
        {
        }
    }

    private static bool IsExpectedTeardownException(Exception exception) => exception is
        JSException or InvalidOperationException or OperationCanceledException;
}
