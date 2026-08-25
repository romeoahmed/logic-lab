using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace LogicLab.Web.Components.Editor;

public sealed partial class WorkbenchCommandBar
{
    [Parameter]
    public bool CanCreate { get; set; }

    [Parameter]
    public bool CanAuthor { get; set; }

    [Parameter]
    public bool CanImport { get; set; }

    [Parameter]
    public bool CanPrepareExport { get; set; }

    [Parameter]
    public bool ShowClaim { get; set; }

    [Parameter]
    public bool CanClaim { get; set; }

    [Parameter]
    public bool ShowSave { get; set; }

    [Parameter]
    public bool CanSave { get; set; }

    [Parameter]
    public string ClaimDisplayName { get; set; } = string.Empty;

    [Parameter]
    public bool CanAuthorSteering { get; set; }

    [Parameter]
    public bool CanAuthorArithmetic { get; set; }

    [Parameter]
    public bool CanAuthorHierarchy { get; set; }

    [Parameter]
    public bool CanCompile { get; set; }

    [Parameter]
    public bool CanCreateSession { get; set; }

    [Parameter]
    public bool CanScheduleStimulus { get; set; }

    [Parameter]
    public bool CanStep { get; set; }

    [Parameter]
    public string? ActiveCommand { get; set; }

    [Parameter]
    public EventCallback OnCreate { get; set; }

    [Parameter]
    public EventCallback OnAuthor { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnImport { get; set; }

    [Parameter]
    public EventCallback OnPrepareExport { get; set; }

    [Parameter]
    public EventCallback<string> ClaimDisplayNameChanged { get; set; }

    [Parameter]
    public EventCallback OnClaim { get; set; }

    [Parameter]
    public EventCallback OnSave { get; set; }

    [Parameter]
    public EventCallback OnAuthorSteering { get; set; }

    [Parameter]
    public EventCallback OnAuthorArithmetic { get; set; }

    [Parameter]
    public EventCallback OnAuthorHierarchy { get; set; }

    [Parameter]
    public EventCallback OnCompile { get; set; }

    [Parameter]
    public EventCallback OnCreateSession { get; set; }

    [Parameter]
    public EventCallback OnScheduleStimulus { get; set; }

    [Parameter]
    public EventCallback OnStep { get; set; }

    private bool IsBusy => ActiveCommand is not null;

}
