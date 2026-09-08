using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private WorkbenchCommandBar.CommandBarModel CommandBarModel => new()
    {
        CanCreate = CanCreate,
        ShowHistory = Projection is not null,
        CanUndo = CanUndo,
        CanRedo = CanRedo,
        CanImport = CanImport,
        CanPrepareExport = CanPrepareExport,
        ShowClaim = ShowClaim,
        CanClaim = CanClaim,
        ShowSave = ShowSave,
        CanSave = CanSave,
        ClaimDisplayName = ClaimDisplayName,
        CanCompile = CanCompile,
        CanCreateSession = CanCreateSession,
        CanRestartSession = CanRestartSession,
        CanCloseSession = CanCloseSession,
        CanHotSwapSession = CanHotSwapSession,
        CanStep = CanStep,
        CanRun = CanStep,
        CanPause = CommandsAvailable && IsSimulationRunning,
        ActiveCommand = ActiveCommand,
    };

    private bool CommandsAvailable => Volatile.Read(ref isDisposed) == 0
        && IsInteractive
        && IsCallerAvailable
        && (WorkspaceIdValue is null || Attachment is not null);

    private bool CanCreate => CommandsAvailable
        && WorkspaceIdValue is null
        && Projection is null;

    private bool IsSimulationRunning => Projection?.Simulation?.Run is RunRunningProjection;

    private bool CanMutateWorkspace => CommandsAvailable && !IsSimulationRunning;

    private bool CanPrepareExport => CanMutateWorkspace && Projection is not null;

    private bool ShowClaim => CommandsAvailable
        && CurrentCaller is AuthenticatedWorkspaceCaller
        && Projection?.Durability is SandboxWorkspaceDurabilityProjection;

    private bool CanClaim => ShowClaim && CanMutateWorkspace && ClaimDisplayName.Length != 0;

    private bool ShowSave => CommandsAvailable
        && CurrentCaller is AuthenticatedWorkspaceCaller
        && Projection?.Durability is DurableWorkspaceDurabilityProjection;

    private bool CanSave => ShowSave && CanMutateWorkspace
        && Projection?.Durability is DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Changed,
        };

    private bool HasSaveConflict => ShowSave
        && Projection?.Durability is DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Conflict,
        };

    private bool CanImport => CanMutateWorkspace
        && Projection?.Compilation is not (CompilationQueuedProjection or CompilationRunningProjection);

    private bool CanSetEntryDefinition => CanMutateWorkspace
        && ActiveCommand is null
        && Projection is not null;

    private bool CanEnterDefinitionInstances => Projection is not null
        && SelectedDefinitionId is not null
        && (HierarchyNavigation.Count != 0
            || SelectedDefinitionId == Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId);

    private bool CanCompile => CommandsAvailable
        && Projection is not null
        && Projection.Simulation is not { Run: RunRunningProjection }
        && Projection.Compilation is not CompilationPublishedProjection
        && Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Count > 0;

    private bool CanCreateSession => CommandsAvailable
        && Projection?.Simulation is null
        && Projection?.Compilation is CompilationPublishedProjection;

    private bool CanRestartSession => CommandsAvailable
        && Projection?.Simulation is { Run: not RunRunningProjection }
        && Projection.Compilation is CompilationPublishedProjection;

    private bool CanCloseSession => CommandsAvailable
        && Projection?.Simulation is { Run: not RunRunningProjection };

    private bool CanHotSwapSession => CanRestartSession
        && Projection!.Simulation!.CompilationArtifactKey
            != ((CompilationPublishedProjection)Projection.Compilation).ArtifactKey;

    private bool HasCurrentSimulation =>
        Projection?.Simulation is { Run: not RunRunningProjection } simulation
        && Projection.Compilation is CompilationPublishedProjection compilation
        && simulation.CompilationArtifactKey == compilation.ArtifactKey;

    private bool CanUseSceneProbe => CanMutateWorkspace
        && Projection?.Simulation is not null
        && CurrentSceneHierarchyPath is not null;

    private bool HasProgrammableInputs => Projection?.ProjectRevision.Document
        .EntryCircuitDefinition.ComponentInstances.Any(InputStimulusPanel.IsProgrammableInput) is true;

    private bool CanScheduleStimulus => CommandsAvailable
        && HasCurrentSimulation
        && Projection!.Simulation!.LogicalTime < ulong.MaxValue
        && InputHierarchyPath is not null;

    private bool CanStep => CommandsAvailable
        && HasCurrentSimulation;

    private Task RunWorkbenchCommandAsync(
        WorkbenchCommandBar.WorkbenchCommand command) => command switch
        {
            WorkbenchCommandBar.WorkbenchCommand.Create => RunCommandAsync(
                "create",
                () => CanCreate,
                CreateProject),
            WorkbenchCommandBar.WorkbenchCommand.Undo => RunCommandAsync(
                "undo", () => CanUndo, () => MoveHistoryAsync(undo: true)),
            WorkbenchCommandBar.WorkbenchCommand.Redo => RunCommandAsync(
                "redo", () => CanRedo, () => MoveHistoryAsync(undo: false)),
            WorkbenchCommandBar.WorkbenchCommand.PrepareExport => RunCommandAsync(
                "export",
                () => CanPrepareExport,
                PrepareProjectExport),
            WorkbenchCommandBar.WorkbenchCommand.Claim => RunCommandAsync(
                "claim",
                () => CanClaim,
                ClaimSandboxProject),
            WorkbenchCommandBar.WorkbenchCommand.Save => RunCommandAsync(
                "save",
                () => CanSave,
                SaveDurableProject),
            WorkbenchCommandBar.WorkbenchCommand.Compile => RunCommandAsync(
                "compile",
                () => CanCompile,
                Compile),
            WorkbenchCommandBar.WorkbenchCommand.CreateSession => RunCommandAsync(
                "session",
                () => CanCreateSession,
                CreateSimulationSession),
            WorkbenchCommandBar.WorkbenchCommand.RestartSession => RunCommandAsync(
                "restart", () => CanRestartSession, RestartSimulationSession),
            WorkbenchCommandBar.WorkbenchCommand.CloseSession => RunCommandAsync(
                "close-session", () => CanCloseSession, CloseSimulationSession),
            WorkbenchCommandBar.WorkbenchCommand.HotSwapSession => RunCommandAsync(
                "hot-swap", () => CanHotSwapSession, HotSwapSimulationSession),
            WorkbenchCommandBar.WorkbenchCommand.Step => RunCommandAsync(
                "step",
                () => CanStep,
                Step),
            WorkbenchCommandBar.WorkbenchCommand.StartRun => RunCommandAsync(
                "run", () => CanStep, StartSimulationRun),
            WorkbenchCommandBar.WorkbenchCommand.PauseRun => RunCommandAsync(
                "pause", () => CommandsAvailable && IsSimulationRunning, PauseSimulationRun),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

    private async Task RunCommandAsync(
        string command,
        Func<bool> canExecute,
        Func<Task> operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        ArgumentNullException.ThrowIfNull(canExecute);
        ArgumentNullException.ThrowIfNull(operation);
        if (ActiveCommand is not null || !canExecute())
        {
            return;
        }

        ActiveCommand = command;
        try
        {
            await operation();
        }
        finally
        {
            ActiveCommand = null;
        }
    }
}
