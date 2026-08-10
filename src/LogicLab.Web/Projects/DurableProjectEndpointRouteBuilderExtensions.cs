using System.Security.Claims;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace LogicLab.Web.Projects;

public static class DurableProjectEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapDurableProjectEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(
                "/projects/open",
                async Task<IResult> (
                    ClaimsPrincipal principal,
                    [FromForm] string durableProjectId,
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
                        || durableProjectId.Length > 64)
                    {
                        return Results.BadRequest();
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
                        WorkspaceOpenRejected rejected => Results.LocalRedirect(
                            $"~/projects?error={Uri.EscapeDataString(rejected.Code)}"),
                        _ => throw new InvalidOperationException(
                            "The Workspace open outcome hierarchy is closed."),
                    };
                })
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }
}
