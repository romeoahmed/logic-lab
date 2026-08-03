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

public sealed class LogicLabWebFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environments.Production);
    }
}

[ClassDataSource<LogicLabWebFactory>(Shared = SharedType.PerTestSession)]
public sealed class WebHostSecurityTests(LogicLabWebFactory factory)
{
    private const string ExpectedContentSecurityPolicy =
        "default-src 'self'; "
        + "base-uri 'self'; "
        + "connect-src 'self'; "
        + "font-src 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'; "
        + "img-src 'self' data:; "
        + "object-src 'none'; "
        + "script-src 'self'; "
        + "style-src 'self' 'unsafe-inline'";

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
        using var response = await client.GetAsync(path);
        var contentSecurityPolicy = Header(response, "Content-Security-Policy");

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
            await Assert.That(contentSecurityPolicy)
                .IsEqualTo(ExpectedContentSecurityPolicy);
            await Assert.That(Header(response, "Cross-Origin-Opener-Policy"))
                .IsEqualTo("same-origin");
            await Assert.That(Header(response, "Permissions-Policy"))
                .IsEqualTo("camera=(), geolocation=(), microphone=()");
            await Assert.That(Header(response, "X-Content-Type-Options")).IsEqualTo("nosniff");
            await Assert.That(Header(response, "X-Frame-Options")).IsEqualTo("DENY");
            await Assert.That(Header(response, "Referrer-Policy")).IsEqualTo("no-referrer");
            await Assert.That(contentSecurityPolicy).DoesNotContain("*");
            await Assert.That(contentSecurityPolicy).Contains("frame-ancestors 'none'");
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
            "/_blazor/negotiate?negotiateVersion=1",
            content: null);
        negotiate.EnsureSuccessStatusCode();
        using var payload = await JsonDocument.ParseAsync(
            await negotiate.Content.ReadAsStreamAsync());
        var connectionToken = payload.RootElement.GetProperty("connectionToken").GetString();
        await Assert.That(connectionToken).IsNotNull();

        var webSocketClient = host.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers["Sec-WebSocket-Extensions"] = "permessage-deflate";
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
                                context.Response.Headers["Sec-WebSocket-Extensions"]
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
