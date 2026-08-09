using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

    private static string Header(HttpResponseMessage response, string name)
    {
        return string.Join(' ', response.Headers.GetValues(name));
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
}
