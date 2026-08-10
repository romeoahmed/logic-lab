using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Projects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>]
internal sealed class DurableProjectEndpointTests(LogicLabWebFactory factory)
{
    [Test]
    public async Task Get_Open_ReturnsMethodNotAllowedProblemDetails()
    {
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.GetAsync(
            new Uri("/projects/open", UriKind.Relative));

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.MethodNotAllowed,
            "project_open_method_not_allowed");
        await Assert.That(response.Content.Headers.Allow)
            .IsEquivalentTo([HttpMethod.Post.Method]);
    }

    [Test]
    public async Task Head_Open_ReturnsMethodNotAllowedProblemMetadata()
    {
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            new Uri("/projects/open", UriKind.Relative));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.MethodNotAllowed);
            await Assert.That(response.Content.Headers.ContentType?.MediaType)
                .IsEqualTo("application/problem+json");
            await Assert.That(response.Content.Headers.Allow)
                .IsEquivalentTo([HttpMethod.Post.Method]);
            await Assert.That(await response.Content.ReadAsByteArrayAsync())
                .IsEmpty();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Get_Projects_SuccessDisablesSharedCaching(bool hasProjects)
    {
        DurableProjectSummaryV1[] items = hasProjects
            ?
            [
                new DurableProjectSummaryV1(
                    new DurableProjectId("project-cache"),
                    new DurableDisplayName("Cache boundary")),
            ]
            : [];
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(
                    new RecordingCatalog(new DurableProjectPage(items, next: null)));
            }));
        using var client = host.CreateHttpsClient();

        using var response = await client.GetAsync(
            new Uri("/projects", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        using (Assert.Multiple())
        {
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task Get_Projects_WithOpaqueCursor_PassesTrustedContextAndRendersHtml()
    {
        const string projectedDisplayName = "Projected 项目";
        var catalog = new RecordingCatalog(
            new DurableProjectPage(
                [
                    new DurableProjectSummaryV1(
                        new DurableProjectId("project-rendered"),
                        new DurableDisplayName(projectedDisplayName)),
                ],
                next: null));
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(catalog);
            }));
        using var client = host.CreateHttpsClient();

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
            await Assert.That(html).Contains(HtmlEncoder.Default.Encode(projectedDisplayName));
            await Assert.That(catalog.CallCount).IsEqualTo(1);
            await Assert.That(catalog.SubjectId?.Value)
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
        using var client = host.CreateHttpsClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.GetAsync(
            new Uri("/projects?after=first&after=second", UriKind.Relative));

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.UnprocessableEntity,
            "project_catalog_request_invalid");
        await Assert.That(catalog.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Get_Projects_WithEmptyCursor_ReturnsCursorInvalidProblemDetailsWithoutCatalogAccess()
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
        using var client = host.CreateHttpsClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.GetAsync(
            new Uri("/projects?after=", UriKind.Relative));

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.UnprocessableEntity,
            "project_catalog_cursor_invalid");
        await Assert.That(catalog.CallCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("workspace_not_found", HttpStatusCode.NotFound)]
    [Arguments("workspace_admission_rejected", HttpStatusCode.TooManyRequests)]
    [Arguments("workspace_internal_defect", HttpStatusCode.InternalServerError)]
    [Arguments("workspace_infrastructure_failure", HttpStatusCode.ServiceUnavailable)]
    [Arguments("compilation_invalid", HttpStatusCode.UnprocessableEntity)]
    [Arguments("compilation_policy_exhausted", HttpStatusCode.UnprocessableEntity)]
    [Arguments("compilation_cancelled", HttpStatusCode.ServiceUnavailable)]
    [Arguments("compilation_infrastructure_failure", HttpStatusCode.ServiceUnavailable)]
    [Arguments("compilation_internal_defect", HttpStatusCode.InternalServerError)]
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
        using var client = host.CreateHttpsClient();

        using var response = await PostOpenAsync(client, "project-a");

        await WebTestHttp.AssertProblemDetailsAsync(response, expectedStatus, code);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    [Arguments(InvalidProjectId.Missing)]
    [Arguments(InvalidProjectId.Empty)]
    [Arguments(InvalidProjectId.Oversized)]
    public Task Post_OpenWithInvalidProjectId_ReturnsRequestInvalidProblemDetails(
        InvalidProjectId invalidProjectId)
        => AssertMalformedOpenRequest(invalidProjectId switch
        {
            InvalidProjectId.Missing => null,
            InvalidProjectId.Empty => string.Empty,
            InvalidProjectId.Oversized => new string('a', 65),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidProjectId)),
        });

    [Test]
    public async Task Post_OpenWithNonFormContentType_ReturnsRequestInvalidProblemDetails()
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
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Add("RequestVerificationToken", form.RequestToken);

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "project_open_request_invalid");
        await Assert.That(workspace.Request).IsNull();
    }

    [Test]
    public async Task Post_OpenWithUnknownFormCharset_ReturnsRequestInvalidProblemDetailsWithoutWorkspaceAccess()
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
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new StringContent("durableProjectId=project-a"),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/x-www-form-urlencoded")
        {
            CharSet = "no-such-charset",
        };
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Add("RequestVerificationToken", form.RequestToken);

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "project_open_request_invalid");
        await Assert.That(workspace.Request).IsNull();
    }

    [Test]
    public async Task Post_OpenWithInvalidUtf8FormBytes_ReturnsRequestInvalidProblemDetailsWithoutWorkspaceAccess()
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
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var antiforgeryContent = new FormUrlEncodedContent(
            [new("__RequestVerificationToken", form.RequestToken)]);
        var antiforgeryBytes = await antiforgeryContent.ReadAsByteArrayAsync();
        var prefix = "durableProjectId="u8;
        var body = new byte[prefix.Length + 2 + antiforgeryBytes.Length];
        prefix.CopyTo(body);
        body[prefix.Length] = 0xff;
        body[prefix.Length + 1] = (byte)'&';
        antiforgeryBytes.CopyTo(body, prefix.Length + 2);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/x-www-form-urlencoded");
        request.Headers.Add("Cookie", form.Cookie);

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "project_open_request_invalid");
        await Assert.That(workspace.Request).IsNull();
    }

    [Test]
    public async Task Post_OpenWithMalformedMultipartBoundary_ReturnsRequestInvalidProblemDetailsWithoutWorkspaceAccess()
    {
        var loader = new FailOnCallDurableProjectLoader();
        await using var workspace = new CountingOpenWorkspace(loader);
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new SingleProjectCatalog());
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("malformed multipart")),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("multipart/form-data");
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Add("RequestVerificationToken", form.RequestToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "project_open_request_invalid");
        using (Assert.Multiple())
        {
            await Assert.That(workspace.CallCount).IsEqualTo(0);
            await Assert.That(loader.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("project_catalog_infrastructure_failure", HttpStatusCode.ServiceUnavailable)]
    [Arguments("project_catalog_internal_defect", HttpStatusCode.InternalServerError)]
    public async Task Get_Projects_CatalogFailure_ReturnsUncacheableProblemDetails(
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
        using var client = host.CreateHttpsClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.GetAsync(
            new Uri("/projects", UriKind.Relative));

        await WebTestHttp.AssertProblemDetailsAsync(response, expectedStatus, reason);
        using (Assert.Multiple())
        {
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
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
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("durableProjectId", "project-a"),
                new("__RequestVerificationToken", form.RequestToken),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);

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

    [Test]
    public async Task Post_OpenWithoutAntiforgeryToken_ReturnsProblemDetailsWithoutWorkspaceAccess()
    {
        await using var workspace = new RecordingOpenWorkspace();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.CreateHttpsClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
                [new("durableProjectId", "project-a")]),
        };
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "antiforgery_validation_failed");
        await Assert.That(workspace.Request).IsNull();
    }

    [Test]
    public async Task Post_OpenWithInvalidAntiforgeryToken_ReturnsProblemDetailsWithoutWorkspaceAccess()
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
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("durableProjectId", "project-a"),
                new("__RequestVerificationToken", "invalid-token"),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "antiforgery_validation_failed");
        await Assert.That(workspace.Request).IsNull();
    }

    [Test]
    public async Task Post_OpenRequestBodyLimit_IsInclusiveAndUsesProblemDetails()
    {
        const int maximumBodyBytes = 4096;
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
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");

        using var accepted = await PostSizedOpenAsync(
            client,
            form,
            maximumBodyBytes);
        using var rejected = await PostSizedOpenAsync(
            client,
            form,
            maximumBodyBytes + 1);

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await WebTestHttp.AssertProblemDetailsAsync(
            rejected,
            HttpStatusCode.RequestEntityTooLarge,
            "request_body_too_large");
        await Assert.That(workspace.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Post_OpenRateLimit_RejectsWithDedicatedProblemDetailsWithoutAdditionalWorkspaceAccess()
    {
        var permitLimit = DurableProjectIngressPolicy.Default.OpenPermitLimit;
        var loader = new NotFoundDurableProjectLoader();
        await using var workspace = new CountingOpenWorkspace(loader);
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureProjectsOnlyAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new SingleProjectCatalog());
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        for (var attempt = 0; attempt < permitLimit; attempt++)
        {
            using var admitted = await PostProtectedOpenAsync(
                client,
                form,
                "project-a");
            await Assert.That(admitted.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        var workspaceCallsBeforeRejection = workspace.CallCount;
        var loaderCallsBeforeRejection = loader.CallCount;
        using var rejected = await PostProtectedOpenAsync(
            client,
            form,
            "project-a");
        await WebTestHttp.AssertProblemDetailsAsync(
            rejected,
            HttpStatusCode.TooManyRequests,
            "project_open_rate_limit_exceeded");
        using (Assert.Multiple())
        {
            await Assert.That(workspaceCallsBeforeRejection)
                .IsEqualTo(permitLimit);
            await Assert.That(loaderCallsBeforeRejection)
                .IsEqualTo(permitLimit);
            await Assert.That(workspace.CallCount)
                .IsEqualTo(workspaceCallsBeforeRejection);
            await Assert.That(loader.CallCount)
                .IsEqualTo(loaderCallsBeforeRejection);
            await Assert.That((rejected.Headers.RetryAfter?.Delta).GetValueOrDefault())
                .IsGreaterThan(TimeSpan.Zero);
        }

        var accountForm = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");
        using var accountResponse = await PostInvalidLoginAsync(
            client,
            accountForm);
        await Assert.That(accountResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Post_OpenRateLimit_ExhaustedSubjectDoesNotConsumeAnotherSubjectAdmission()
    {
        const string subjectA = "subject-a";
        const string subjectB = "subject-b";
        var permitLimit = DurableProjectIngressPolicy.Default.OpenPermitLimit;
        var loader = new NotFoundDurableProjectLoader();
        await using var workspace = new CountingOpenWorkspace(loader);
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new SingleProjectCatalog());
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var clientA = host.CreateHttpsClient();
        using var clientB = host.CreateHttpsClient();
        clientA.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.SubjectHeaderName,
            subjectA);
        clientB.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.SubjectHeaderName,
            subjectB);
        var openFormA = await WebTestHttp.GetAntiforgeryFormAsync(
            clientA,
            "/projects");
        var openFormB = await WebTestHttp.GetAntiforgeryFormAsync(
            clientB,
            "/projects");

        for (var attempt = 0; attempt < permitLimit; attempt++)
        {
            using var admitted = await PostProtectedOpenAsync(
                clientA,
                openFormA,
                "project-a");
            await Assert.That(admitted.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        using var rejectedA = await PostProtectedOpenAsync(
            clientA,
            openFormA,
            "project-a");
        await WebTestHttp.AssertProblemDetailsAsync(
            rejectedA,
            HttpStatusCode.TooManyRequests,
            "project_open_rate_limit_exceeded");
        var workspaceCallsAfterA = workspace.CallCount;
        var loaderCallsAfterA = loader.CallCount;

        using var admittedB = await PostProtectedOpenAsync(
            clientB,
            openFormB,
            "project-a");

        using (Assert.Multiple())
        {
            await Assert.That(admittedB.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(workspaceCallsAfterA).IsEqualTo(permitLimit);
            await Assert.That(loaderCallsAfterA).IsEqualTo(permitLimit);
            await Assert.That(workspace.CallCount).IsEqualTo(permitLimit + 1);
            await Assert.That(loader.CallCount).IsEqualTo(permitLimit + 1);
            await Assert.That(loader.SubjectIds.Take(permitLimit)
                    .All(subjectId => subjectId.Value == subjectA))
                .IsTrue();
            await Assert.That(loader.SubjectIds[^1].Value)
                .IsEqualTo(subjectB);
        }
    }

    [Test]
    public async Task Post_OpenWithoutAuthentication_ReturnsAuthenticationRequiredProblemDetails()
    {
        await using var workspace = new RecordingOpenWorkspace();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.CreateHttpsClient();

        using var response = await PostOpenFromAnonymousFormAsync(client);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.Unauthorized,
            "authentication_required");
        using (Assert.Multiple())
        {
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(workspace.Request).IsNull();
        }
    }

    [Test]
    public async Task Post_OpenWithAuthenticatedPrincipalMissingSubject_BeyondPermitLimitAlwaysReturnsAuthenticationRequired()
    {
        var attemptCount = checked(
            DurableProjectIngressPolicy.Default.OpenPermitLimit + 1);
        await using var workspace = new RecordingOpenWorkspace();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthenticationWithoutSubject(services);
                services.RemoveAll<IEditorWorkspace>();
                services.AddSingleton<IEditorWorkspace>(workspace);
            }));
        using var client = host.CreateHttpsClient();

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            using var response = await PostOpenFromAnonymousFormAsync(client);
            await WebTestHttp.AssertProblemDetailsAsync(
                response,
                HttpStatusCode.Unauthorized,
                "authentication_required");
            await Assert.That(response.Headers.Location).IsNull();
        }

        using (Assert.Multiple())
        {
            await Assert.That(workspace.Request).IsNull();
            await Assert.That(workspace.CallCount).IsEqualTo(0);
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

    private static void ConfigureProjectsOnlyAuthentication(
        IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    ProjectsOnlyAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme =
                    ProjectsOnlyAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions,
                ProjectsOnlyAuthenticationHandler>(
                ProjectsOnlyAuthenticationHandler.SchemeName,
                configureOptions: null);
    }

    private static void ConfigureAuthenticationWithoutSubject(
        IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    MissingSubjectAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme =
                    MissingSubjectAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions,
                MissingSubjectAuthenticationHandler>(
                MissingSubjectAuthenticationHandler.SchemeName,
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
        using var client = host.CreateHttpsClient();

        using var response = await PostOpenAsync(client, durableProjectId);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "project_open_request_invalid");
        await Assert.That(workspace.Request).IsNull();
    }

    private static async Task<HttpResponseMessage> PostOpenAsync(
        HttpClient client,
        string? durableProjectId)
    {
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", form.RequestToken),
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
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostProtectedOpenAsync(
        HttpClient client,
        AntiforgeryForm form,
        string durableProjectId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
                [new("durableProjectId", durableProjectId)]),
        };
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Add("RequestVerificationToken", form.RequestToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostInvalidLoginAsync(
        HttpClient client,
        AntiforgeryForm form)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/account/login", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("_handler", "login"),
                new("Input.Email", "not-an-email"),
                new("Input.Password", "invalid"),
                new("__RequestVerificationToken", form.RequestToken),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostSizedOpenAsync(
        HttpClient client,
        AntiforgeryForm form,
        int bodyLength)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("durableProjectId", "project-a"),
            new("__RequestVerificationToken", form.RequestToken),
            new("padding", string.Empty),
        };
        using var unpadded = new FormUrlEncodedContent(values);
        var paddingLength = checked(
            bodyLength - (int)(unpadded.Headers.ContentLength
                ?? throw new InvalidOperationException(
                    "The form content did not expose its length.")));
        if (paddingLength < 0)
        {
            throw new InvalidOperationException(
                "The requested body length is smaller than the form envelope.");
        }

        values[^1] = new("padding", new string('x', paddingLength));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(values),
        };
        if (request.Content.Headers.ContentLength != bodyLength)
        {
            throw new InvalidOperationException(
                "The encoded form did not reach the requested byte boundary.");
        }

        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostOpenFromAnonymousFormAsync(
        HttpClient client)
    {
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/projects/open", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("durableProjectId", "project-a"),
                new("__RequestVerificationToken", form.RequestToken),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        return await client.SendAsync(request);
    }

    private sealed class SingleProjectCatalog : IDurableProjectCatalog
    {
        public Task<DurableProjectListOutcome> ListAsync(
            AuthenticatedSubjectId subjectId,
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

        public AuthenticatedSubjectId? SubjectId { get; private set; }

        public DurableProjectPageRequest? Request { get; private set; }

        public Task<DurableProjectListOutcome> ListAsync(
            AuthenticatedSubjectId subjectId,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SubjectId = subjectId;
            Request = request;
            return Task.FromResult(outcome);
        }
    }

    private sealed class RejectedCatalog(string reason) : IDurableProjectCatalog
    {
        public Task<DurableProjectListOutcome> ListAsync(
            AuthenticatedSubjectId subjectId,
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
        public int CallCount { get; private set; }

        public OpenWorkspaceRequest? Request { get; private set; }

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            return base.OpenAsync(
                new CreateSandbox("Reopened project", "Main"),
                cancellationToken);
        }
    }

    private sealed class CountingOpenWorkspace(IDurableProjectLoader loader)
        : DelegatingEditorWorkspace(durableProjectLoader: loader)
    {
        public int CallCount { get; private set; }

        public override Task<WorkspaceOpenOutcome> OpenAsync(
            OpenWorkspaceRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return base.OpenAsync(request, cancellationToken);
        }
    }

    private sealed class NotFoundDurableProjectLoader : IDurableProjectLoader
    {
        private readonly List<AuthenticatedSubjectId> subjectIds = [];

        public int CallCount { get; private set; }

        public AuthenticatedSubjectId[] SubjectIds => [.. subjectIds];

        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            subjectIds.Add(request.SubjectId);
            return Task.FromResult<DurableProjectOpenRepositoryOutcome>(
                new DurableProjectOpenNotFound());
        }
    }

    private sealed class FailOnCallDurableProjectLoader : IDurableProjectLoader
    {
        public int CallCount { get; private set; }

        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException(
                "Malformed input must be rejected before Durable Project loading.");
        }
    }

    internal enum InvalidProjectId
    {
        Missing,
        Empty,
        Oversized,
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
        public const string SubjectHeaderName = "X-Test-Subject";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers[SubjectHeaderName].ToString();
            if (string.IsNullOrEmpty(subject))
            {
                subject = "subject-http";
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim(ClaimTypes.Name, "endpoint user"),
                ],
                SchemeName);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class ProjectsOnlyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            loggerFactory,
            encoder)
    {
        public const string SchemeName = "ProjectsOnlyEndpointTests";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Context.Request.Path.StartsWithSegments("/account"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "subject-http")],
                SchemeName);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class MissingSubjectAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            loggerFactory,
            encoder)
    {
        public const string SchemeName = "MissingSubjectEndpointTests";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "subjectless user")],
                SchemeName);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
