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
    private const string CacheControl = "private, no-store";

    public static IEndpointConventionBuilder MapProjectExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var downloadEndpoint = endpoints.MapMethods(
            DownloadPattern,
            [HttpMethods.Get],
            async Task<IResult> (
                string token,
                HttpContext httpContext,
                ClaimsPrincipal principal,
                IProjectExportDownloads downloads,
                CancellationToken cancellationToken) =>
            {
                DisableCaching(httpContext);

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

                var caller = WorkspaceCallerAdapter.FromTransferPrincipal(principal);
                if (caller is null)
                {
                    return LogicLabProblemDetails.Create(
                        httpContext,
                        WorkspaceOutcomeReasons.ExportExpired);
                }

                var outcome = await downloads.RedeemAsync(
                    new ProjectExportDownloadRequest(
                        exportTicket,
                        caller),
                    cancellationToken);
                if (outcome is ProjectExportDownloadRejected rejected)
                {
                    return LogicLabProblemDetails.Create(
                        httpContext,
                        rejected.Code);
                }

                var downloaded = (ProjectExportDownloaded)outcome;
                return TypedResults.File(
                    downloaded.Content,
                    ContentType,
                    FileName,
                    enableRangeProcessing: false);
            });

        endpoints.MapFallback(DownloadPattern, IResult (HttpContext httpContext) =>
        {
            DisableCaching(httpContext);
            httpContext.Response.Headers.Allow = HttpMethods.Get;
            if (HttpMethods.IsHead(httpContext.Request.Method))
            {
                httpContext.Response.StatusCode =
                    StatusCodes.Status405MethodNotAllowed;
                httpContext.Response.ContentType = "application/problem+json";
                return Results.Empty;
            }

            return LogicLabProblemDetails.Create(
                httpContext,
                MethodNotAllowedCode);
        });

        return downloadEndpoint;
    }

    private static void DisableCaching(HttpContext httpContext) =>
        httpContext.Response.Headers.CacheControl = CacheControl;
}
