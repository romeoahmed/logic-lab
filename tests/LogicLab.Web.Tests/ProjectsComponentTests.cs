using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Projects;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class ProjectsComponentTests
{
    [Test]
    public async Task Projects_AuthenticatedPage_RendersProjectionAndAuthorizedOpenForms()
    {
        await using var context = WebTestContext.CreateBunitContext();
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
        var openButtons = rendered.FindAll("button[type='submit']");
        using (Assert.Multiple())
        {
            await Assert.That(items.Select(item => item.GetAttribute("data-project-id")!))
                .IsEquivalentTo(
                    ["project-a", "project-b"],
                    CollectionOrdering.Matching);
            await Assert.That(rendered.FindAll("[data-project-id] > bdi[dir='auto']")
                    .Select(item => item.TextContent))
                .IsEquivalentTo(
                    ["Alpha", "项目 B"],
                    CollectionOrdering.Matching);
            await Assert.That(rendered.FindAll("form[action='/projects/open']").Count)
                .IsEqualTo(2);
            await Assert.That(openButtons.Count).IsEqualTo(2);
            await Assert.That(openButtons.Select((button, index) =>
                    button.GetAttribute("aria-label")?.Contains(
                        page.Items[index].DisplayName.Value,
                        StringComparison.Ordinal) is true)
                .All(static hasAuthoredName => hasAuthoredName)).IsTrue();
            await Assert.That(rendered.FindAll("input[name='durableProjectId']")
                    .Select(input => input.GetAttribute("value")!))
                .IsEquivalentTo(
                    ["project-a", "project-b"],
                    CollectionOrdering.Matching);
            await Assert.That(rendered.Find("[data-projects-next]")
                    .GetAttribute("href"))
                .IsEqualTo("/projects?after=protected%2B%2Fcursor%3D");
        }
    }

    [Test]
    public async Task Projects_EmptyCatalog_OffersIntentionalSandboxRecovery()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Services.AddAuthorizationCore();
        context.Services.AddAntiforgery();
        context.Services.AddSingleton(new DurableProjectCatalogPageState
        {
            Page = new DurableProjectPage([], next: null),
        });

        var rendered = context.Render<LogicLab.Web.Components.Pages.Projects>();
        var empty = rendered.Find("[data-catalog-empty][role='status']");
        var recovery = empty.QuerySelector("a[href='/editor']")
            ?? throw new InvalidOperationException(
                "The empty catalog did not provide a Sandbox recovery action.");

        using (Assert.Multiple())
        {
            await Assert.That(string.IsNullOrWhiteSpace(recovery.TextContent)).IsFalse();
            await Assert.That(rendered.FindAll("[data-project-id]")).IsEmpty();
        }
    }

    [Test]
    public async Task Projects_UserAuthoredBidirectionalName_RendersInIsolationBoundary()
    {
        const string displayName = "LTR مشروع \u202eABC";
        await using var context = WebTestContext.CreateBunitContext();
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
