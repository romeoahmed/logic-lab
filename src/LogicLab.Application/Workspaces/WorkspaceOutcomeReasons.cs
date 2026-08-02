using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

internal static class WorkspaceOutcomeReasons
{
    public const string NoScheduledStimulus = "no_scheduled_stimulus";
    public const string ProjectRevisionPreconditionFailed =
        "project_revision_precondition_failed";
    public const string SessionPreconditionFailed = "session_precondition_failed";
    public const string WorkspaceAdmissionRejected = "workspace_admission_rejected";
    public const string WorkspaceCancelled = "workspace_cancelled";
    public const string WorkspaceInternalDefect = "workspace_internal_defect";
    public const string WorkspaceInfrastructureFailure = "workspace_infrastructure_failure";
    public const string WorkspaceExpired = "workspace_expired";
    public const string WorkspaceNotFound = "workspace_not_found";

    public static string FromSimulation(SimulationFailureReason reason)
    {
        return reason switch
        {
            SimulationFailureReason.ZeroTimeOscillation => "zero_time_oscillation",
            SimulationFailureReason.SimulationResourceLimit => "simulation_resource_limit",
            SimulationFailureReason.SimulationCancelled => "simulation_cancelled",
            SimulationFailureReason.SimulationInfrastructureFailure =>
                "simulation_infrastructure_failure",
            SimulationFailureReason.SimulationInternalDefect => "simulation_internal_defect",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }
}
