using System.Net;
using System.Security.Claims;
using System.Text.Json;
using LogicLab.Web.Identity;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
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
        builder.UseEnvironment(Environments.Staging);
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

[ClassDataSource<LogicLabWebFactory>]
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
    public async Task Get_AnonymousBootstrap_IssuesCallerCookieOnlyOnPrivateEditorResponse()
    {
        using var client = factory.CreateHttpsClient();

        using var staticAsset = await client.GetAsync(
            new Uri("/app.css", UriKind.Relative));
        using var editor = await client.GetAsync(
            new Uri("/editor", UriKind.Relative));
        var staticCookies = SetCookies(staticAsset);
        var editorCallerCookies = SetCookies(editor).Where(value => value.StartsWith(
                $"{AnonymousWorkspaceCallerMiddleware.CookieName}=",
                StringComparison.Ordinal))
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(staticAsset.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(staticCookies.Any(value => value.StartsWith(
                    $"{AnonymousWorkspaceCallerMiddleware.CookieName}=",
                    StringComparison.Ordinal)))
                .IsFalse();
            await Assert.That(editor.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(editor.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(editor.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(editorCallerCookies).HasSingleItem();
            await Assert.That(editorCallerCookies.FirstOrDefault() ?? string.Empty)
                .Contains("secure; samesite=lax; httponly", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Test]
    public async Task Get_AnonymousBootstrapCookieRotation_RejectsBeforeIssuingAnotherIdentity()
    {
        var policy = new AnonymousWorkspaceIngressPolicy(
            issuancePermitLimit: 2,
            issuanceWindow: TimeSpan.FromMinutes(1));
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AnonymousWorkspaceIngressPolicy>();
                services.AddSingleton(policy);
                services.TryAddEnumerable(ServiceDescriptor.Singleton<
                    IStartupFilter,
                    AuthenticatedRequestStartupFilter>());
            }));
        using var firstJar = host.CreateHttpsClient();
        using var secondJar = host.CreateHttpsClient();
        using var discardedCookieJar = host.CreateHttpsClient();

        using var first = await firstJar.GetAsync(new Uri("/editor", UriKind.Relative));
        var callerCookie = SetCookies(first).Single(IsAnonymousCallerCookie)
            .Split(';', StringSplitOptions.TrimEntries)[0];
        using var existingIdentityRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/editor", UriKind.Relative));
        existingIdentityRequest.Headers.Add("Cookie", callerCookie);
        using var existingIdentity = await firstJar.SendAsync(existingIdentityRequest);
        using var second = await secondJar.GetAsync(new Uri("/editor", UriKind.Relative));
        using var authenticatedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/editor", UriKind.Relative));
        authenticatedRequest.Headers.Add(
            AuthenticatedRequestStartupFilter.HeaderName,
            "true");
        using var authenticated = await discardedCookieJar.SendAsync(authenticatedRequest);
        using var rejected = await discardedCookieJar.GetAsync(
            new Uri("/editor", UriKind.Relative));

        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(existingIdentity.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(authenticated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(SetCookies(first).Count(IsAnonymousCallerCookie))
                .IsEqualTo(1);
            await Assert.That(SetCookies(existingIdentity).Any(IsAnonymousCallerCookie))
                .IsFalse();
            await Assert.That(SetCookies(second).Count(IsAnonymousCallerCookie))
                .IsEqualTo(1);
            await Assert.That(SetCookies(authenticated).Any(IsAnonymousCallerCookie))
                .IsFalse();
            await Assert.That(SetCookies(rejected).Any(IsAnonymousCallerCookie))
                .IsFalse();
            await Assert.That(rejected.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(rejected.Headers.CacheControl?.NoStore).IsTrue();
        }

        await WebTestHttp.AssertProblemDetailsAsync(
            rejected,
            HttpStatusCode.TooManyRequests,
            "anonymous_workspace_ingress_exceeded");
        var retryAfter = rejected.Headers.RetryAfter?.Delta;
        await Assert.That(retryAfter).IsNotNull();
        await Assert.That(retryAfter.GetValueOrDefault())
            .IsGreaterThan(TimeSpan.Zero);
    }

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

    [Test, Timeout(30_000)]
    public async Task Connect_InteractiveServer_DoesNotNegotiateWebSocketCompression(
        CancellationToken cancellationToken)
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
            content: null,
            cancellationToken);
        negotiate.EnsureSuccessStatusCode();
        using var payload = await JsonDocument.ParseAsync(
            await negotiate.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var connectionToken = payload.RootElement.GetProperty("connectionToken").GetString();
        await Assert.That(connectionToken).IsNotNull();

        var webSocketClient = host.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers.SecWebSocketExtensions = "permessage-deflate";
        using var socket = await webSocketClient.ConnectAsync(
            new Uri($"ws://localhost/_blazor?id={Uri.EscapeDataString(connectionToken!)}"),
            cancellationToken);
        var negotiatedExtensions = await capture.Extensions.WaitAsync(cancellationToken);

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
    public async Task Post_AuthenticationEntries_UseIndependentBoundedPoliciesWithProblemDetails()
    {
        var policy = AccountIngressPolicy.Default;
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateHttpsClient();
        var loginForm = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");
        var registrationForm = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/register");

        for (var attempt = 0; attempt < policy.LoginPermitLimit; attempt++)
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

        for (var attempt = 0; attempt < policy.RegistrationPermitLimit; attempt++)
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
        const int maximumBodyBytes = AccountIngressPolicy.MaximumRequestBodyBytes;
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, path);

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
        await WebTestHttp.AssertProblemDetailsAsync(
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

    private static string[] SetCookies(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? [.. values]
            : [];
    }

    private static bool IsAnonymousCallerCookie(string value) => value.StartsWith(
        $"{AnonymousWorkspaceCallerMiddleware.CookieName}=",
        StringComparison.Ordinal);

    private static async Task<HttpResponseMessage> PostInvalidIdentityFormAsync(
        HttpClient client,
        string path,
        string formName,
        AntiforgeryForm form,
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
        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.ParseAdd("text/html");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostSizedIdentityFormAsync(
        HttpClient client,
        string path,
        string formName,
        AntiforgeryForm form,
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

        request.Headers.Add("Cookie", form.Cookie);
        request.Headers.Accept.ParseAdd("text/html");
        return await client.SendAsync(request);
    }

    private static async Task AssertRateLimitProblemDetails(
        HttpResponseMessage response)
    {
        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.TooManyRequests,
            "authentication_rate_limit_exceeded");
        var retryAfter = response.Headers.RetryAfter?.Delta;
        await Assert.That(retryAfter).IsNotNull();
        await Assert.That(retryAfter.GetValueOrDefault())
            .IsGreaterThan(TimeSpan.Zero);
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

    private sealed class AuthenticatedRequestStartupFilter : IStartupFilter
    {
        public const string HeaderName = "X-LogicLab-Test-Authenticated";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Headers.ContainsKey(HeaderName))
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "test-subject")],
                            authenticationType: "test"));
                    }

                    await nextMiddleware();
                });
                next(app);
            };
        }
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

}
