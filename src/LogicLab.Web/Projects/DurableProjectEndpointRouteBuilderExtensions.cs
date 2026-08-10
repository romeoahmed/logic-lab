using System.Security.Claims;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace LogicLab.Web.Projects;

public static class DurableProjectEndpointRouteBuilderExtensions
{
    internal const string OpenPath = "/projects/open";

    private const int MaximumDurableProjectIdLength = 64;
    private const int MaximumOpenRequestBodyBytes = 4096;

    public static IEndpointConventionBuilder MapDurableProjectEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var openEndpoint = endpoints.MapPost(
                OpenPath,
                async Task<IResult> (
                    HttpContext httpContext,
                    ClaimsPrincipal principal,
                    IEditorWorkspace workspace,
                    CancellationToken cancellationToken) =>
                {
                    var subjectValue = principal.FindFirst(
                        ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(subjectValue))
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.AuthenticationRequiredCode);
                    }

                    if (!httpContext.Request.HasFormContentType)
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.ProjectOpenRequestInvalidCode);
                    }

                    var form = await httpContext.Request.ReadFormAsync(
                        cancellationToken);
                    var durableProjectId = form.TryGetValue(
                            "durableProjectId",
                            out var durableProjectIds)
                        && durableProjectIds.Count == 1
                            ? durableProjectIds[0]
                            : null;
                    if (string.IsNullOrEmpty(durableProjectId)
                        || durableProjectId.Length > MaximumDurableProjectIdLength)
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.ProjectOpenRequestInvalidCode);
                    }

                    var outcome = await workspace.OpenAsync(
                        new OpenDurable(
                            new DurableProjectId(durableProjectId),
                            new AuthenticatedWorkspaceCaller(
                                new AuthenticatedSubjectId(subjectValue))),
                        cancellationToken);
                    return outcome switch
                    {
                        WorkspaceOpened opened => Results.LocalRedirect(
                            $"~/editor/{Uri.EscapeDataString(opened.WorkspaceId.Value)}"),
                        WorkspaceOpenRejected rejected =>
                            LogicLabProblemDetails.Create(httpContext, rejected.Code),
                        _ => throw new InvalidOperationException(
                            "The Workspace open outcome hierarchy is closed."),
                    };
                })
            .RequireAuthorization()
            .DisableCookieRedirect()
            .WithMetadata(new RequestSizeLimitAttribute(
                MaximumOpenRequestBodyBytes))
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        endpoints.MapFallback(OpenPath, IResult (HttpContext httpContext) =>
        {
            httpContext.Response.Headers.Allow = HttpMethods.Post;
            if (HttpMethods.IsHead(httpContext.Request.Method))
            {
                httpContext.Response.StatusCode =
                    StatusCodes.Status405MethodNotAllowed;
                httpContext.Response.ContentType = "application/problem+json";
                return Results.Empty;
            }

            return LogicLabProblemDetails.Create(
                httpContext,
                LogicLabProblemDetails.ProjectOpenMethodNotAllowedCode);
        });

        return openEndpoint;
    }
}
