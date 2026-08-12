using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public static class WorkspaceOutcomeReasons
{
    public const string AuthenticationRequired = "authentication_required";
    public const string HotSwapIncompatible = "hot_swap_incompatible";
    public const string CompilationGenerationUnavailable =
        "compilation_generation_unavailable";

    public const string RunGenerationPreconditionFailed =
        "run_generation_precondition_failed";
    public const string NoScheduledStimulus = "no_scheduled_stimulus";
    public const string ProjectRevisionPreconditionFailed =
        "project_revision_precondition_failed";
    public const string ProjectionVersionPreconditionFailed =
        "projection_version_precondition_failed";
    public const string StaleWorkspaceAttachment = "stale_workspace_attachment";
    public const string IdempotencyKeyConflict = "idempotency_key_conflict";
    public const string IdempotencyWindowExpired = "idempotency_window_expired";
    public const string DurableClaimUnresolved = "durable_claim_unresolved";
    public const string BuildFingerprintMismatch = "build_fingerprint_mismatch";
    public const string SessionPreconditionFailed = "session_precondition_failed";
    public const string DurableDisplayNameInvalid = "durable_display_name_invalid";
    public const string WorkspaceAdmissionRejected = "workspace_admission_rejected";
    public const string WorkspaceCancelled = "workspace_cancelled";
    public const string WorkspaceInternalDefect = "workspace_internal_defect";
    public const string WorkspaceInfrastructureFailure = "workspace_infrastructure_failure";
    public const string WorkspaceExpired = "workspace_expired";
    public const string WorkspaceNotFound = "workspace_not_found";
    public const string ExportCapacityUnavailable = "export_capacity_unavailable";
    public const string ExportExpired = "export_expired";

    internal static RetryDisposition RetryFor(string code)
    {
        return code switch
        {
            StaleWorkspaceAttachment or IdempotencyWindowExpired =>
                RetryDisposition.Reattach,
            ProjectRevisionPreconditionFailed
                or ProjectionVersionPreconditionFailed
                or CompilationGenerationUnavailable
                or SessionPreconditionFailed
                or RunGenerationPreconditionFailed
                or HotSwapIncompatible => RetryDisposition.RefreshProjection,
            _ => RetryDisposition.DoNotRetry,
        };
    }

    internal static string FromSimulation(SimulationFailureReason reason)
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
