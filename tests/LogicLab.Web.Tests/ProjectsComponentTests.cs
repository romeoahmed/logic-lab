using System.Security.Claims;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Web.Tests;

internal sealed class ProjectsComponentTests
{
    [Test]
    public async Task Projects_AuthenticatedPage_RendersProjectionAndAuthorizedOpenForms()
    {
        await using var context = new BunitContext();
        var catalog = new RecordingCatalog(new DurableProjectPage(
            [
                new DurableProjectSummaryV1(
                    new DurableProjectId("project-a"),
                    new DurableDisplayName("Alpha")),
                new DurableProjectSummaryV1(
                    new DurableProjectId("project-b"),
                    new DurableDisplayName("项目 B")),
            ],
            new ProjectCatalogCursor("protected+/cursor=")));
        context.Services.AddAuthorizationCore();
        context.Services.AddAntiforgery();
        context.Services.AddSingleton<AuthenticationStateProvider>(
            new FixedAuthenticationStateProvider("subject-7"));
        context.Services.AddSingleton<IDurableProjectCatalog>(catalog);
        context.Services.AddSingleton<IDurableProjectCatalogAuthorization,
            AuthenticatedCatalogAuthorization>();
        context.Services.AddSingleton(WorkspacePolicy.Default);

        var rendered = context.Render<LogicLab.Web.Components.Pages.Projects>();
        await rendered.WaitForStateAsync(() => catalog.Context is not null);

        var items = rendered.FindAll("[data-project-id]");
        using (Assert.Multiple())
        {
            await Assert.That(items.Select(item => item.GetAttribute("data-project-id")!))
                .IsEquivalentTo(["project-a", "project-b"]);
            await Assert.That(rendered.FindAll("[data-project-id] > span")
                    .Select(item => item.TextContent))
                .IsEquivalentTo(["Alpha", "项目 B"]);
            await Assert.That(rendered.FindAll("form[action='/projects/open']").Count)
                .IsEqualTo(2);
            await Assert.That(rendered.FindAll("input[name='durableProjectId']")
                    .Select(input => input.GetAttribute("value")!))
                .IsEquivalentTo(["project-a", "project-b"]);
            await Assert.That(rendered.Find("[data-projects-next]")
                    .GetAttribute("href"))
                .IsEqualTo("/projects?after=protected%2B%2Fcursor%3D");
            await Assert.That(((AuthenticatedWorkspaceCaller)catalog.Context!.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-7");
            await Assert.That(catalog.Request?.PageSize)
                .IsEqualTo(WorkspacePolicy.Default.DurableProjectCatalogLimits.PageItems);
        }
    }

    [Test]
    public async Task Projects_AfterQuery_PassesOpaqueCursorWithoutInterpretingIt()
    {
        await using var context = new BunitContext();
        var catalog = new RecordingCatalog(new DurableProjectPage([], next: null));
        context.Services.AddAuthorizationCore();
        context.Services.AddAntiforgery();
        context.Services.AddSingleton<AuthenticationStateProvider>(
            new FixedAuthenticationStateProvider("subject-7"));
        context.Services.AddSingleton<IDurableProjectCatalog>(catalog);
        context.Services.AddSingleton<IDurableProjectCatalogAuthorization,
            AuthenticatedCatalogAuthorization>();
        context.Services.AddSingleton(WorkspacePolicy.Default);

        var navigationManager = context.Services
            .GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter(
            "after",
            "opaque+/cursor="));
        _ = context.Render<LogicLab.Web.Components.Pages.Projects>();
        await Assert.That(catalog.Request?.After?.Value)
            .IsEqualTo("opaque+/cursor=");
    }

    private sealed class RecordingCatalog(DurableProjectListOutcome outcome)
        : IDurableProjectCatalog
    {
        public DurableProjectCatalogCallContext? Context { get; private set; }

        public DurableProjectPageRequest? Request { get; private set; }

        public Task<DurableProjectListOutcome> ListAsync(
            DurableProjectCatalogCallContext context,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            Context = context;
            Request = request;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FixedAuthenticationStateProvider(string subjectId)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subjectId),
                    new Claim(ClaimTypes.Name, "catalog user"),
                ],
                authenticationType: "Tests");
            return Task.FromResult(new AuthenticationState(
                new ClaimsPrincipal(identity)));
        }
    }

    private sealed class AuthenticatedCatalogAuthorization
        : IDurableProjectCatalogAuthorization
    {
        public ValueTask<bool> AuthorizeListAsync(
            AuthenticatedSubjectId subjectId,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
