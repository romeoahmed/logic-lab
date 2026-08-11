namespace LogicLab.Application.Workspaces;

public static class DurableProjectCatalogOutcomeReasons
{
    public const string RequestInvalid = "project_catalog_request_invalid";
    public const string CursorInvalid = "project_catalog_cursor_invalid";
    public const string Cancelled = "project_catalog_cancelled";
    public const string InfrastructureFailure = "project_catalog_infrastructure_failure";
    public const string InternalDefect = "project_catalog_internal_defect";
}
