using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Components.Editor;

public sealed record WorkbenchViewState(
    bool CanCreate,
    bool CanAuthor,
    bool CanCompile,
    bool CanCreateSession,
    bool CanScheduleStimulus,
    bool CanStep)
{
    public static WorkbenchViewState StaticShell { get; } = new(
        CanCreate: false,
        CanAuthor: false,
        CanCompile: false,
        CanCreateSession: false,
        CanScheduleStimulus: false,
        CanStep: false);

    public static WorkbenchViewState ReadyToCreate { get; } = StaticShell with
    {
        CanCreate = true,
    };

    public static WorkbenchViewState EmptyProject { get; } = StaticShell with
    {
        CanAuthor = true,
    };

    public static WorkbenchViewState CircuitReady { get; } = StaticShell with
    {
        CanCompile = true,
    };

    public static WorkbenchViewState Compiled { get; } = StaticShell with
    {
        CanCreateSession = true,
    };

    public static WorkbenchViewState SessionReady { get; } = StaticShell with
    {
        CanScheduleStimulus = true,
    };

    public static WorkbenchViewState StepReady { get; } = StaticShell with
    {
        CanStep = true,
    };

    public static WorkbenchViewState From(
        WorkspaceProjection projection,
        bool stimulusScheduled)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.Simulation is not null)
        {
            return stimulusScheduled ? StepReady : SessionReady;
        }

        if (projection.Compilation.Status is CompilationPublicationStatus.Published)
        {
            return Compiled;
        }

        return projection.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Count == 0
            ? EmptyProject
            : CircuitReady;
    }
}
