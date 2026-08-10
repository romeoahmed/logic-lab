using System.Security.Claims;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace LogicLab.Web.Projects;

public static class DurableProjectEndpointRouteBuilderExtensions
{
    private const int MaximumDurableProjectIdLength = 64;

    public static IEndpointConventionBuilder MapDurableProjectEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(
                "/projects/open",
                async Task<IResult> (
                    HttpContext httpContext,
                    ClaimsPrincipal principal,
                    [FromForm] string? durableProjectId,
                    IEditorWorkspace workspace,
                    CancellationToken cancellationToken) =>
                {
                    var subjectValue = principal.FindFirst(
                        ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(subjectValue))
                    {
                        return Results.Unauthorized();
                    }

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
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }
}
