using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace LogicLab.Web.Components.Editor;

public sealed partial class WorkbenchCommandBar
{
    private bool projectOptionsOpened;

    [Parameter]
    public CommandBarModel Model { get; set; } = new();

    [Parameter]
    public EventCallback<WorkbenchCommand> OnCommand { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnImport { get; set; }

    [Parameter]
    public EventCallback<string> ClaimDisplayNameChanged { get; set; }

    private bool IsBusy => Model.ActiveCommand is not null;

    private void ToggleProjectOptions() => projectOptionsOpened = !projectOptionsOpened;

    private Task InvokeProjectCommandAsync(WorkbenchCommand command)
    {
        projectOptionsOpened = false;
        return OnCommand.InvokeAsync(command);
    }

    public enum WorkbenchCommand
    {
        Create,
        PrepareExport,
        Claim,
        Save,
        Compile,
        CreateSession,
        ScheduleStimulus,
        Step,
    }

    public sealed record CommandBarModel
    {
        public bool CanCreate { get; init; }

        public bool CanImport { get; init; }

        public bool CanPrepareExport { get; init; }

        public bool ShowClaim { get; init; }

        public bool CanClaim { get; init; }

        public bool ShowSave { get; init; }

        public bool CanSave { get; init; }

        public string ClaimDisplayName { get; init; } = string.Empty;

        public bool CanCompile { get; init; }

        public bool CanCreateSession { get; init; }

        public bool CanScheduleStimulus { get; init; }

        public bool CanStep { get; init; }

        public string? ActiveCommand { get; init; }
    }
}
