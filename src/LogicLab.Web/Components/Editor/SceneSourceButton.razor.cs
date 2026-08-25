using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SceneSourceButton
{
    [Parameter, EditorRequired]
    public SceneSourceRefV1 Source { get; set; } = null!;

    [Parameter]
    public bool Selected { get; set; }

    [Parameter]
    public bool RequestFocus { get; set; }

    [Parameter, EditorRequired]
    public IReadOnlyDictionary<string, object> NavigationAttributes { get; set; } = null!;

    [Parameter]
    public EventCallback<SceneSourceRefV1> OnFocus { get; set; }

    [Parameter]
    public EventCallback<ActivateSceneSemanticActionV1> OnActivate { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    private ElementReference buttonElement;
    private bool focusApplied;

    protected override void OnParametersSet()
    {
        if (!RequestFocus)
        {
            focusApplied = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RequestFocus || focusApplied)
        {
            return;
        }

        focusApplied = true;
        await buttonElement.FocusAsync();
    }

    private Task ActivateAsync(MouseEventArgs eventArgs)
    {
        var selectionMode = "replace";
        if (eventArgs.CtrlKey || eventArgs.MetaKey)
        {
            selectionMode = "toggle";
        }
        else if (eventArgs.ShiftKey)
        {
            selectionMode = "add";
        }

        return OnActivate.InvokeAsync(new ActivateSceneSemanticActionV1(
            Source,
            selectionMode));
    }
}
