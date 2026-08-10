using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>(Shared = SharedType.PerTestSession)]
internal sealed class DurableProjectEndpointTests(LogicLabWebFactory factory)
{
    [Test]
    public async Task Post_OpenWithAuthenticatedAntiforgeryForm_ReauthorizesAndRedirectsToWorkspace()
    {
        await using var workspace = new RecordingOpenWorkspace();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        configureOptions: null);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new SingleProjectCatalog());
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.Server.CreateClient();
        using var pageResponse = await client.GetAsync(
            new Uri("/projects", UriKind.Relative));
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();
        var requestToken = ExtractAttributeAfter(
            html,
            "name=\"__RequestVerificationToken\"",
            "value");
        var antiforgeryCookie = pageResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal))
            .Split(';', 2)[0];
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("durableProjectId", "project-a"),
                new("__RequestVerificationToken", requestToken),
            ]),
        };
        request.Headers.Add("Cookie", antiforgeryCookie);

        using var response = await client.SendAsync(request);

        var open = (await Assert.That(workspace.Request)
            .IsTypeOf<OpenDurable>())!;
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location?.OriginalString)
                .StartsWith("/editor/");
            await Assert.That(open.DurableProjectId.Value).IsEqualTo("project-a");
            await Assert.That(((AuthenticatedWorkspaceCaller)open.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-http");
        }
    }

    private static string ExtractAttributeAfter(
        string html,
        string marker,
        string attributeName)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException($"Markup did not contain {marker}.");
        }

        var prefix = $"{attributeName}=\"";
        var valueStart = html.IndexOf(prefix, markerIndex, StringComparison.Ordinal);
        if (valueStart < 0)
        {
            throw new InvalidOperationException(
                $"Markup did not contain {attributeName} after {marker}.");
        }

        valueStart += prefix.Length;
        var valueEnd = html.IndexOf('"', valueStart);
        return html[valueStart..valueEnd];
    }

    private sealed class SingleProjectCatalog : IDurableProjectCatalog
    {
        public Task<DurableProjectListOutcome> ListAsync(
            DurableProjectCatalogCallContext context,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            DurableProjectListOutcome outcome = new DurableProjectPage(
                [
                    new DurableProjectSummaryV1(
                        new DurableProjectId("project-a"),
                        new DurableDisplayName("Alpha")),
                ],
                next: null);
            return Task.FromResult(outcome);
        }
    }

    private sealed class RecordingOpenWorkspace : DelegatingEditorWorkspace
    {
        public OpenWorkspaceRequest? Request { get; private set; }

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return base.OpenAsync(
                new CreateSandbox("Reopened project", "Main"),
                cancellationToken);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            loggerFactory,
            encoder)
    {
        public const string SchemeName = "ProjectEndpointTests";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "subject-http"),
                    new Claim(ClaimTypes.Name, "endpoint user"),
                ],
                SchemeName);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
