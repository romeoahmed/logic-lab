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

    private WorkflowAction[] WorkflowActions =>
    [
        new(WorkbenchCommand.Create, "create", "CreateSandbox", Model.CanCreate),
        new(WorkbenchCommand.Compile, "compile", "Compile", Model.CanCompile),
        new(WorkbenchCommand.CreateSession, "session", "CreateSession", Model.CanCreateSession),
        new(WorkbenchCommand.ScheduleStimulus, "stimulus", "InputsHigh", Model.CanScheduleStimulus),
        new(WorkbenchCommand.Step, "step", "Step", Model.CanStep),
        new(WorkbenchCommand.StartRun, "run", "Run", Model.CanRun),
        new(WorkbenchCommand.PauseRun, "pause", "Pause", Model.CanPause),
        new(WorkbenchCommand.HotSwapSession, "hot-swap", "HotSwapSession", Model.CanHotSwapSession),
        new(WorkbenchCommand.RestartSession, "restart", "RestartSession", Model.CanRestartSession, false),
        new(WorkbenchCommand.CloseSession, "close-session", "CloseSession", Model.CanCloseSession, false),
    ];

    private readonly record struct WorkflowAction(
        WorkbenchCommand Command, string Key, string Label, bool Available, bool Primary = true);

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
        RestartSession,
        CloseSession,
        HotSwapSession,
        ScheduleStimulus,
        Step,
        StartRun,
        PauseRun,
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

        public bool CanRestartSession { get; init; }

        public bool CanCloseSession { get; init; }

        public bool CanHotSwapSession { get; init; }

        public bool CanScheduleStimulus { get; init; }

        public bool CanStep { get; init; }

        public bool CanRun { get; init; }

        public bool CanPause { get; init; }

        public string? ActiveCommand { get; init; }
    }
}
