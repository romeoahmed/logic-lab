using System.Security.Claims;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;

namespace LogicLab.Web.Projects;

public static class DurableProjectCatalogPageEndpointExtensions
{
    public static RazorComponentsEndpointConventionBuilder
        AddDurableProjectCatalogPageAdapter(
            this RazorComponentsEndpointConventionBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.Finally(endpoint =>
        {
            if (endpoint.Metadata
                    .OfType<ComponentTypeMetadata>()
                    .LastOrDefault()?.Type
                != typeof(Components.Pages.Projects))
            {
                return;
            }

            var next = endpoint.RequestDelegate
                ?? throw new InvalidOperationException(
                    "The Durable Project Catalog endpoint has no request delegate.");
            endpoint.RequestDelegate = httpContext => InvokeAsync(httpContext, next);
        });
        return endpoints;
    }

    private static async Task InvokeAsync(
        HttpContext httpContext,
        RequestDelegate next)
    {
        var subjectValue = httpContext.User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(subjectValue))
        {
            await LogicLabProblemDetails.Create(
                httpContext,
                "authentication_required").ExecuteAsync(httpContext);
            return;
        }

        var afterValues = httpContext.Request.Query["after"];
        if (afterValues.Count > 1)
        {
            await LogicLabProblemDetails.Create(
                httpContext,
                "project_catalog_request_invalid").ExecuteAsync(httpContext);
            return;
        }

        var services = httpContext.RequestServices;
        var catalog = services.GetRequiredService<IDurableProjectCatalog>();
        var authorization = services
            .GetRequiredService<IDurableProjectCatalogAuthorization>();
        var policy = services.GetRequiredService<WorkspacePolicy>();
        var afterValue = afterValues.Count == 0 ? null : afterValues[0];
        var cursor = string.IsNullOrEmpty(afterValue)
            ? null
            : new ProjectCatalogCursor(afterValue);
        var outcome = await catalog.ListAsync(
            new DurableProjectCatalogCallContext(
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId(subjectValue)),
                authorization),
            new DurableProjectPageRequest(
                policy.DurableProjectCatalogLimits.PageItems,
                cursor),
            httpContext.RequestAborted);
        if (outcome is DurableProjectListRejected rejected)
        {
            await LogicLabProblemDetails.Create(
                httpContext,
                rejected.Reason).ExecuteAsync(httpContext);
            return;
        }

        var page = outcome as DurableProjectPage
            ?? throw new InvalidOperationException(
                "The Durable Project Catalog outcome hierarchy is closed.");
        services.GetRequiredService<DurableProjectCatalogPageState>().Page = page;
        await next(httpContext);
    }
}

internal sealed class DurableProjectCatalogPageState
{
    public DurableProjectPage? Page { get; set; }
}
