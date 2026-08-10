using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Projects;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Web.Tests;

internal sealed class ProjectsComponentTests
{
    [Test]
    public async Task Projects_AuthenticatedPage_RendersProjectionAndAuthorizedOpenForms()
    {
        await using var context = new BunitContext();
        var page = new DurableProjectPage(
            [
                new DurableProjectSummaryV1(
                    new DurableProjectId("project-a"),
                    new DurableDisplayName("Alpha")),
                new DurableProjectSummaryV1(
                    new DurableProjectId("project-b"),
                    new DurableDisplayName("项目 B")),
            ],
            new ProjectCatalogCursor("protected+/cursor="));
        context.Services.AddAuthorizationCore();
        context.Services.AddAntiforgery();
        context.Services.AddSingleton(new DurableProjectCatalogPageState
        {
            Page = page,
        });

        var rendered = context.Render<LogicLab.Web.Components.Pages.Projects>();

        var items = rendered.FindAll("[data-project-id]");
        using (Assert.Multiple())
        {
            await Assert.That(items.Select(item => item.GetAttribute("data-project-id")!))
                .IsEquivalentTo(["project-a", "project-b"]);
            await Assert.That(rendered.FindAll("[data-project-id] > bdi[dir='auto']")
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
        }
    }

    [Test]
    public async Task Projects_UserAuthoredBidirectionalName_RendersInIsolationBoundary()
    {
        const string displayName = "LTR مشروع \u202eABC";
        await using var context = new BunitContext();
        context.Services.AddAuthorizationCore();
        context.Services.AddAntiforgery();
        context.Services.AddSingleton(new DurableProjectCatalogPageState
        {
            Page = new DurableProjectPage(
                [
                    new DurableProjectSummaryV1(
                        new DurableProjectId("project-bidi"),
                        new DurableDisplayName(displayName)),
                ],
                next: null),
        });

        var rendered = context.Render<LogicLab.Web.Components.Pages.Projects>();

        var isolatedName = rendered.Find(
            "[data-project-id='project-bidi'] > bdi[dir='auto']");
        await Assert.That(isolatedName.TextContent).IsEqualTo(displayName);
    }
}
