using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SceneToolStrip
{
    [Parameter, EditorRequired]
    public SceneToolV1 ActiveTool { get; set; } = SceneSelectToolV1.Instance;

    [Parameter]
    public SceneHierarchyPathV1? HierarchyPath { get; set; }

    [Parameter]
    public bool CanProbe { get; set; }

    [Parameter]
    public bool CanWire { get; set; } = true;

    [Parameter]
    public EventCallback<SceneToolV1> ActiveToolChanged { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private Task ChangeToolAsync(SceneToolV1 tool) => tool is SceneWireToolV1 && !CanWire
        ? Task.CompletedTask
        : ActiveToolChanged.InvokeAsync(tool);

    private Task SelectProbeToolAsync() => !CanProbe || HierarchyPath is null
        ? Task.CompletedTask
        : ChangeToolAsync(new SceneProbeToolV1(HierarchyPath));
}
