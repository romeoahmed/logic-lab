using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    public async Task Get_Projects_WithOpaqueCursor_PassesTrustedContextAndRendersHtml()
    {
        var catalog = new RecordingCatalog(
            new DurableProjectPage([], next: null));
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(catalog);
            }));
        using var client = host.Server.CreateClient();

        using var response = await client.GetAsync(
            new Uri(
                "/projects?after=protected%2B%2Fcursor%3D",
                UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Content.Headers.ContentType?.MediaType)
                .IsEqualTo("text/html");
            await Assert.That(html).Contains("Durable Projects");
            await Assert.That(catalog.CallCount).IsEqualTo(1);
            await Assert.That(((AuthenticatedWorkspaceCaller)catalog.Context!.Caller)
                    .SubjectId.Value)
                .IsEqualTo("subject-http");
            await Assert.That(catalog.Request?.After?.Value)
                .IsEqualTo("protected+/cursor=");
            await Assert.That(catalog.Request?.PageSize)
                .IsEqualTo(WorkspacePolicy.Default.DurableProjectCatalogLimits.PageItems);
        }
    }

    [Test]
    public async Task Get_Projects_WithRepeatedCursor_ReturnsRequestInvalidProblemDetails()
    {
        var catalog = new RecordingCatalog(
            new DurableProjectPage([], next: null));
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(catalog);
            }));
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.GetAsync(
            new Uri("/projects?after=first&after=second", UriKind.Relative));

        await AssertProblemDetails(
            response,
            HttpStatusCode.UnprocessableEntity,
            "project_catalog_request_invalid");
        await Assert.That(catalog.CallCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("workspace_not_found", HttpStatusCode.NotFound)]
    [Arguments("workspace_admission_rejected", HttpStatusCode.TooManyRequests)]
    [Arguments("workspace_internal_defect", HttpStatusCode.InternalServerError)]
    [Arguments("workspace_infrastructure_failure", HttpStatusCode.ServiceUnavailable)]
    public async Task Post_OpenRejected_ReturnsMappedProblemDetails(
        string code,
        HttpStatusCode expectedStatus)
    {
        await using var workspace = new RejectingOpenWorkspace(code);
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new SingleProjectCatalog());
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.Server.CreateClient();

        using var response = await PostOpenAsync(client, "project-a");

        await AssertProblemDetails(response, expectedStatus, code);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public Task Post_OpenWithoutProjectId_ReturnsRequestInvalidProblemDetails()
        => AssertMalformedOpenRequest(null);

    [Test]
    public Task Post_OpenWithEmptyProjectId_ReturnsRequestInvalidProblemDetails()
        => AssertMalformedOpenRequest(string.Empty);

    [Test]
    public Task Post_OpenWithOversizedProjectId_ReturnsRequestInvalidProblemDetails()
        => AssertMalformedOpenRequest(new string('a', 65));

    [Test]
    [Arguments("project_catalog_infrastructure_failure", HttpStatusCode.ServiceUnavailable)]
    [Arguments("project_catalog_internal_defect", HttpStatusCode.InternalServerError)]
    public async Task Get_Projects_CatalogFailure_ReturnsMappedProblemDetails(
        string reason,
        HttpStatusCode expectedStatus)
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(
                    new RejectedCatalog(reason));
            }));
        using var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.GetAsync(
            new Uri("/projects", UriKind.Relative));

        await AssertProblemDetails(response, expectedStatus, reason);
    }

    [Test]
    public async Task Post_OpenWithAuthenticatedAntiforgeryForm_ReauthorizesAndRedirectsToWorkspace()
    {
        await using var workspace = new RecordingOpenWorkspace();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
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

    private static void ConfigureAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                configureOptions: null);
    }

    private async Task AssertMalformedOpenRequest(string? durableProjectId)
    {
        await using var workspace = new RecordingOpenWorkspace();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new SingleProjectCatalog());
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.Server.CreateClient();

        using var response = await PostOpenAsync(client, durableProjectId);

        await AssertProblemDetails(
            response,
            HttpStatusCode.BadRequest,
            "project_open_request_invalid");
        await Assert.That(workspace.Request).IsNull();
    }

    private static async Task<HttpResponseMessage> PostOpenAsync(
        HttpClient client,
        string? durableProjectId)
    {
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
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", requestToken),
        };
        if (durableProjectId is not null)
        {
            formValues.Add(new("durableProjectId", durableProjectId));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(formValues),
        };
        request.Headers.Add("Cookie", antiforgeryCookie);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }

    private static async Task AssertProblemDetails(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;
        var traceId = root.GetProperty("traceId").GetString();

        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("status").GetInt32())
                .IsEqualTo((int)expectedStatus);
            await Assert.That(root.GetProperty("code").GetString())
                .IsEqualTo(expectedCode);
            await Assert.That(root.GetProperty("type").GetString())
                .IsEqualTo($"https://logiclab.example/problems/{expectedCode}");
            await Assert.That(string.IsNullOrWhiteSpace(
                    root.GetProperty("title").GetString()))
                .IsFalse();
            await Assert.That(IsCorrelationToken(traceId)).IsTrue();
        }
    }

    private static bool IsCorrelationToken(string? value)
    {
        return value is { Length: >= 16 and <= 64 }
            && IsLowercaseLetterOrDigit(value[0])
            && value.All(character => IsLowercaseLetterOrDigit(character)
                || character is '_' or '-');
    }

    private static bool IsLowercaseLetterOrDigit(char value)
    {
        return value is >= 'a' and <= 'z' or >= '0' and <= '9';
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

    private sealed class RecordingCatalog(DurableProjectListOutcome outcome)
        : IDurableProjectCatalog
    {
        public int CallCount { get; private set; }

        public DurableProjectCatalogCallContext? Context { get; private set; }

        public DurableProjectPageRequest? Request { get; private set; }

        public Task<DurableProjectListOutcome> ListAsync(
            DurableProjectCatalogCallContext context,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Context = context;
            Request = request;
            return Task.FromResult(outcome);
        }
    }

    private sealed class RejectedCatalog(string reason) : IDurableProjectCatalog
    {
        public Task<DurableProjectListOutcome> ListAsync(
            DurableProjectCatalogCallContext context,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<DurableProjectListOutcome>(
                new DurableProjectListRejected(
                    reason,
                    [],
                    RetryDisposition.DoNotRetry));
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

    private sealed class RejectingOpenWorkspace(string code)
        : DelegatingEditorWorkspace
    {
        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                new WorkspaceOpenRejected(
                    code,
                    [],
                    RetryDisposition.DoNotRetry));
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
