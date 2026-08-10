using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace LogicLab.Web.Tests;

internal sealed class LogicLabWebFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environments.Production);
    }
}

internal static class LogicLabWebFactoryClient
{
    private static readonly Uri HttpsBaseAddress = new("https://localhost/");

    public static HttpClient CreateHttpsClient(
        this WebApplicationFactory<Program> factory)
    {
        var client = factory.Server.CreateClient();
        client.BaseAddress = HttpsBaseAddress;
        return client;
    }
}

[ClassDataSource<LogicLabWebFactory>(Shared = SharedType.PerTestSession)]
internal sealed class WebHostSecurityTests(LogicLabWebFactory factory)
{
    private static readonly string[] ExpectedContentSecurityPolicyDirectives =
    [
        "base-uri 'self'",
        "connect-src 'self'",
        "default-src 'self'",
        "font-src 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'",
        "img-src 'self' data:",
        "object-src 'none'",
        "script-src 'self'",
        "style-src 'self' 'unsafe-inline'",
    ];

    [Test]
    [Arguments("/editor", HttpStatusCode.OK)]
    [Arguments("/Error", HttpStatusCode.OK)]
    [Arguments("/missing-resource", HttpStatusCode.NotFound)]
    [Arguments("/app.css", HttpStatusCode.OK)]
    public async Task Get_PageErrorAndStaticAsset_ApplyCentralSecurityHeaders(
        string path,
        HttpStatusCode expectedStatus)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var contentSecurityPolicy = Header(response, "Content-Security-Policy");
        var contentSecurityPolicyDirectives = CanonicalizeContentSecurityPolicy(
            contentSecurityPolicy);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
            await Assert.That(contentSecurityPolicyDirectives)
                .IsEquivalentTo(ExpectedContentSecurityPolicyDirectives);
            await Assert.That(Header(response, "Cross-Origin-Opener-Policy"))
                .IsEqualTo("same-origin");
            await Assert.That(Header(response, "Permissions-Policy"))
                .IsEqualTo("camera=(), geolocation=(), microphone=()");
            await Assert.That(Header(response, "X-Content-Type-Options")).IsEqualTo("nosniff");
            await Assert.That(Header(response, "X-Frame-Options")).IsEqualTo("DENY");
            await Assert.That(Header(response, "Referrer-Policy")).IsEqualTo("no-referrer");
            await Assert.That(contentSecurityPolicy).DoesNotContain("*");
        }
    }

    [Test]
    public async Task Connect_InteractiveServer_DoesNotNegotiateWebSocketCompression()
    {
        var capture = new WebSocketHandshakeCapture();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.TryAddSingleton(capture);
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IStartupFilter, HandshakeCaptureStartupFilter>());
            }));
        using var client = host.CreateClient();
        using var negotiate = await client.PostAsync(
            new Uri("/_blazor/negotiate?negotiateVersion=1", UriKind.Relative),
            content: null);
        negotiate.EnsureSuccessStatusCode();
        using var payload = await JsonDocument.ParseAsync(
            await negotiate.Content.ReadAsStreamAsync());
        var connectionToken = payload.RootElement.GetProperty("connectionToken").GetString();
        await Assert.That(connectionToken).IsNotNull();

        var webSocketClient = host.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers.SecWebSocketExtensions = "permessage-deflate";
        using var socket = await webSocketClient.ConnectAsync(
            new Uri($"ws://localhost/_blazor?id={Uri.EscapeDataString(connectionToken!)}"),
            CancellationToken.None);
        var negotiatedExtensions = await capture.Extensions.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(negotiatedExtensions).IsNullOrEmpty();
    }

    [Test]
    public async Task Get_ProjectsWithoutAuthentication_ChallengesToLocalLogin()
    {
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(
            new Uri("/projects", UriKind.Relative));

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location?.PathAndQuery)
                .IsEqualTo("/account/login?ReturnUrl=%2Fprojects");
        }
    }

    [Test]
    public async Task Post_ProjectsOpenEndpoint_RequiresAntiforgeryValidation()
    {
        var endpointDataSource = factory.Services
            .GetRequiredService<EndpointDataSource>();
        var endpoint = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/projects/open");

        await Assert.That(endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()?
                .RequiresValidation)
            .IsTrue();
    }

    [Test]
    public async Task Post_AuthenticationEntries_UseIndependentBoundedPoliciesWithProblemDetails()
    {
        const int loginPermitLimit = 10;
        const int registrationPermitLimit = 5;
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateHttpsClient();
        var loginForm = await PrepareIdentityFormAsync(client, "/account/login");
        var registrationForm = await PrepareIdentityFormAsync(
            client,
            "/account/register");

        for (var attempt = 0; attempt < loginPermitLimit; attempt++)
        {
            using var accepted = await PostInvalidIdentityFormAsync(
                client,
                "/account/login",
                "login",
                loginForm,
                $"invalid-login-{attempt}");
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using var rejectedLogin = await PostInvalidIdentityFormAsync(
            client,
            "/account/login",
            "login",
            loginForm,
            "invalid-login-rejected");
        await AssertRateLimitProblemDetails(rejectedLogin);

        for (var attempt = 0; attempt < registrationPermitLimit; attempt++)
        {
            using var accepted = await PostInvalidIdentityFormAsync(
                client,
                "/account/register",
                "register",
                registrationForm,
                $"invalid-registration-{attempt}");
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using var rejectedRegistration = await PostInvalidIdentityFormAsync(
            client,
            "/account/register",
            "register",
            registrationForm,
            "invalid-registration-rejected");
        await AssertRateLimitProblemDetails(rejectedRegistration);
    }

    [Test]
    [Arguments("/account/login", "login")]
    [Arguments("/account/register", "register")]
    public async Task Post_AuthenticationEntry_RequestBodyLimitIsInclusiveAndUsesProblemDetails(
        string path,
        string formName)
    {
        const int maximumBodyBytes = 4096;
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateHttpsClient();
        var form = await PrepareIdentityFormAsync(client, path);

        using var accepted = await PostSizedIdentityFormAsync(
            client,
            path,
            formName,
            form,
            maximumBodyBytes);
        using var rejected = await PostSizedIdentityFormAsync(
            client,
            path,
            formName,
            form,
            maximumBodyBytes + 1);

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProblemDetails(
            rejected,
            HttpStatusCode.RequestEntityTooLarge,
            "request_body_too_large");
    }

    [Test]
    public async Task AntiforgeryCookie_HttpsResponse_IncludesSecureAttribute()
    {
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync(
            new Uri("https://localhost/account/login"));
        response.EnsureSuccessStatusCode();
        var antiforgeryCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal));

        await Assert.That(antiforgeryCookie.Split(';', StringSplitOptions.TrimEntries))
            .Contains("secure", StringComparer.OrdinalIgnoreCase);
    }

    private static string Header(HttpResponseMessage response, string name)
    {
        return string.Join(' ', response.Headers.GetValues(name));
    }

    private static async Task<PreparedIdentityForm> PrepareIdentityFormAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var token = ExtractAttributeAfter(
            html,
            "name=\"__RequestVerificationToken\"",
            "value");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return new PreparedIdentityForm(token, cookie);
    }

    private static async Task<HttpResponseMessage> PostInvalidIdentityFormAsync(
        HttpClient client,
        string path,
        string formName,
        PreparedIdentityForm form,
        string email)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("_handler", formName),
                new("__RequestVerificationToken", form.RequestToken),
                new("Input.Email", email),
                new("Input.Password", "invalid"),
                new("Input.ConfirmPassword", "different"),
            ]),
        };
        request.Headers.Add("Cookie", form.AntiforgeryCookie);
        request.Headers.Accept.ParseAdd("text/html");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostSizedIdentityFormAsync(
        HttpClient client,
        string path,
        string formName,
        PreparedIdentityForm form,
        int bodyLength)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("_handler", formName),
            new("__RequestVerificationToken", form.RequestToken),
            new("Input.Email", "not-an-email"),
            new("Input.Password", "invalid"),
            new("Input.ConfirmPassword", "different"),
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
            new Uri(path, UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(values),
        };
        if (request.Content.Headers.ContentLength != bodyLength)
        {
            throw new InvalidOperationException(
                "The encoded form did not reach the requested byte boundary.");
        }

        request.Headers.Add("Cookie", form.AntiforgeryCookie);
        request.Headers.Accept.ParseAdd("text/html");
        return await client.SendAsync(request);
    }

    private static async Task AssertRateLimitProblemDetails(
        HttpResponseMessage response)
    {
        await AssertProblemDetails(
            response,
            HttpStatusCode.TooManyRequests,
            "authentication_rate_limit_exceeded");
        var retryAfter = response.Headers.RetryAfter?.Delta;
        await Assert.That(retryAfter).IsNotNull();
        await Assert.That(retryAfter.GetValueOrDefault())
            .IsGreaterThan(TimeSpan.Zero);
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
            await Assert.That(string.IsNullOrWhiteSpace(traceId)).IsFalse();
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

    private static string[] CanonicalizeContentSecurityPolicy(string policy)
    {
        return
        [
            .. policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(directive =>
                    directive.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToDictionary(
                    parts => parts[0],
                    parts => parts[1..],
                    StringComparer.Ordinal)
                .Select(pair =>
                    $"{pair.Key} {string.Join(' ', pair.Value.Order(StringComparer.Ordinal))}"),
        ];
    }

    private sealed class WebSocketHandshakeCapture
    {
        private readonly TaskCompletionSource<string?> extensions = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Extensions => extensions.Task;

        public void Capture(string? value) => extensions.TrySetResult(value);
    }

    private sealed class HandshakeCaptureStartupFilter(WebSocketHandshakeCapture capture)
        : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path == "/_blazor"
                        && context.WebSockets.IsWebSocketRequest)
                    {
                        context.Response.OnStarting(() =>
                        {
                            capture.Capture(
                                context.Response.Headers.SecWebSocketExtensions
                                    .FirstOrDefault());
                            return Task.CompletedTask;
                        });
                    }

                    await nextMiddleware();
                });
                next(app);
            };
        }
    }

    private sealed record PreparedIdentityForm(
        string RequestToken,
        string AntiforgeryCookie);
}
