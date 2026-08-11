namespace LogicLab.Engine.Compilation;

public static class CompilationOutcomeReasons
{
    public const string Invalid = "compilation_invalid";
    public const string PolicyExhausted = "compilation_policy_exhausted";
    public const string Cancelled = "compilation_cancelled";
    public const string InfrastructureFailure = "compilation_infrastructure_failure";
    public const string InternalDefect = "compilation_internal_defect";
}
