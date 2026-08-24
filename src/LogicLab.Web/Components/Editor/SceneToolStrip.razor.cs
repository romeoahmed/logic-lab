using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Editor;

public partial class SceneToolStrip : IAsyncDisposable
{
    private ElementReference toolbarElement;
    private SceneToolStripInterop? interop;
    private bool toolbarNavigationReady;
    private int isDisposed;

    [Parameter, EditorRequired]
    public IReadOnlyList<ScenePlaceOptionV1> PlaceOptions { get; set; } = [];

    [Parameter, EditorRequired]
    public SceneToolV1 ActiveTool { get; set; } = SceneSelectToolV1.Instance;

    [Parameter]
    public SceneHierarchyPathV1? HierarchyPath { get; set; }

    [Parameter]
    public bool CanProbe { get; set; }

    [Parameter]
    public EventCallback<SceneToolV1> ActiveToolChanged { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private string ToolbarRole => toolbarNavigationReady ? "toolbar" : "group";

    private string SelectedPlaceOptionId => ActiveTool is ScenePlaceToolV1 place
        ? place.Target switch
        {
            SceneLibraryComponentTargetV1 library =>
                $"library:{library.LibraryId}:{library.ContractId}",
            SceneCircuitDefinitionTargetV1 definition =>
                $"definition:{definition.CircuitDefinitionId}",
            _ => string.Empty,
        }
        : string.Empty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || !RendererInfo.IsInteractive || isDisposed != 0)
        {
            return;
        }

        var candidate = new SceneToolStripInterop(JS);
        try
        {
            await candidate.MountAsync(toolbarElement);
        }
        catch (Exception exception) when (exception is JSException
            or InvalidOperationException
            or OperationCanceledException)
        {
            await candidate.DisposeAsync();
            return;
        }

        if (isDisposed != 0)
        {
            await candidate.DisposeAsync();
            return;
        }

        interop = candidate;
        toolbarNavigationReady = true;
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (Interlocked.Exchange(ref isDisposed, 1) != 0 || interop is null)
        {
            return;
        }

        await interop.DisposeAsync();
    }

    private int? ToolbarTabIndex(bool isEntry) => toolbarNavigationReady
        ? isEntry ? 0 : -1
        : null;

    private Task ChangeToolAsync(SceneToolV1 tool) => ActiveToolChanged.InvokeAsync(tool);

    private Task SelectProbeToolAsync() => !CanProbe || HierarchyPath is null
        ? Task.CompletedTask
        : ChangeToolAsync(new SceneProbeToolV1(HierarchyPath));

    private Task SelectPlaceToolAsync(ChangeEventArgs change)
    {
        var option = PlaceOptions.FirstOrDefault(candidate => string.Equals(
            candidate.Id,
            change.Value?.ToString(),
            StringComparison.Ordinal));
        return option is null ? Task.CompletedTask : ChangeToolAsync(option.Tool);
    }
}
