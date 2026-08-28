using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SceneToolStrip
{
    private const string SelectToolKey = "select";
    private const string WireToolKey = "wire";
    private const string ProbeToolKey = "probe";
    private const string PanToolKey = "pan";

    private ElementReference selectToolElement;
    private ElementReference wireToolElement;
    private ElementReference probeToolElement;
    private ElementReference panToolElement;
    private bool toolbarNavigationReady;
    private bool toolbarHasFocus;
    private string toolbarTabStop = SelectToolKey;
    private string? pendingToolbarFocus;

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

    private string ToolbarRole => toolbarNavigationReady ? "toolbar" : "group";

    private bool ProbeAvailable => CanProbe && HierarchyPath is not null;

    protected override void OnParametersSet()
    {
        if (!ProbeAvailable
            && string.Equals(toolbarTabStop, ProbeToolKey, StringComparison.Ordinal))
        {
            toolbarTabStop = SelectToolKey;
            pendingToolbarFocus = toolbarHasFocus ? SelectToolKey : null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive)
        {
            return;
        }

        if (!toolbarNavigationReady)
        {
            toolbarNavigationReady = true;
            StateHasChanged();
            return;
        }

        if (pendingToolbarFocus is null)
        {
            return;
        }

        var target = pendingToolbarFocus;
        pendingToolbarFocus = null;
        await ToolElement(target).FocusAsync();
    }

    private int? ToolbarTabIndex(string toolKey)
    {
        if (!toolbarNavigationReady)
        {
            return null;
        }

        return string.Equals(toolbarTabStop, toolKey, StringComparison.Ordinal) ? 0 : -1;
    }

    private void TrackToolbarFocus(string toolKey)
    {
        toolbarHasFocus = true;
        toolbarTabStop = toolKey;
    }

    private void ReleaseToolbarFocus() => toolbarHasFocus = false;

    private void MoveToolbarFocus(KeyboardEventArgs eventArgs, string currentToolKey)
    {
        var offset = eventArgs.Key switch
        {
            "ArrowLeft" => -1,
            "ArrowRight" => 1,
            _ => 0,
        };
        if (offset == 0)
        {
            return;
        }

        var toolKeys = AvailableToolKeys();
        var currentIndex = Array.IndexOf(toolKeys, currentToolKey);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = (currentIndex + offset + toolKeys.Length) % toolKeys.Length;
        toolbarTabStop = toolKeys[targetIndex];
        pendingToolbarFocus = toolbarTabStop;
    }

    private string[] AvailableToolKeys() => ProbeAvailable
        ? [SelectToolKey, WireToolKey, ProbeToolKey, PanToolKey]
        : [SelectToolKey, WireToolKey, PanToolKey];

    private ElementReference ToolElement(string toolKey) => toolKey switch
    {
        SelectToolKey => selectToolElement,
        WireToolKey => wireToolElement,
        ProbeToolKey => probeToolElement,
        PanToolKey => panToolElement,
        _ => throw new ArgumentOutOfRangeException(nameof(toolKey)),
    };

    private Task ChangeToolAsync(SceneToolV1 tool) => ActiveToolChanged.InvokeAsync(tool);

    private Task SelectProbeToolAsync() => !CanProbe || HierarchyPath is null
        ? Task.CompletedTask
        : ChangeToolAsync(new SceneProbeToolV1(HierarchyPath));

}
