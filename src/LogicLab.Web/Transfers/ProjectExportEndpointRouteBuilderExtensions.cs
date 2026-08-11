using System.Security.Claims;
using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Transfers;

internal static class ProjectExportEndpointRouteBuilderExtensions
{
    internal const string DownloadPattern = "/downloads/{token}";
    internal const string ContentType = "application/octet-stream";
    internal const string FileName = "logiclab-project.logiclab";
    internal const string MethodNotAllowedCode =
        "export_download_method_not_allowed";

    public static IEndpointConventionBuilder MapProjectExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapMethods(
            DownloadPattern,
            [HttpMethods.Get, HttpMethods.Head],
            async Task<IResult> (
                string token,
                HttpContext httpContext,
                ClaimsPrincipal principal,
                IProjectExportDownloads downloads,
                CancellationToken cancellationToken) =>
            {
                if (!HttpMethods.IsGet(httpContext.Request.Method))
                {
                    httpContext.Response.Headers.Allow = HttpMethods.Get;
                    return LogicLabProblemDetails.Create(
                        httpContext,
                        MethodNotAllowedCode);
                }

                ExportTicket exportTicket;
                try
                {
                    exportTicket = new ExportTicket(token);
                }
                catch (ArgumentException)
                {
                    return LogicLabProblemDetails.Create(
                        httpContext,
                        WorkspaceOutcomeReasons.ExportExpired);
                }

                var outcome = await downloads.RedeemAsync(
                    new ProjectExportDownloadRequest(
                        exportTicket,
                        WorkspaceCallerAdapter.FromPrincipal(principal)),
                    cancellationToken);
                if (outcome is ProjectExportDownloadRejected rejected)
                {
                    return LogicLabProblemDetails.Create(
                        httpContext,
                        rejected.Code);
                }

                var downloaded = (ProjectExportDownloaded)outcome;
                httpContext.Response.Headers.CacheControl = "private, no-store";
                return TypedResults.File(
                    downloaded.Content,
                    ContentType,
                    FileName,
                    enableRangeProcessing: false);
            });
    }
}
