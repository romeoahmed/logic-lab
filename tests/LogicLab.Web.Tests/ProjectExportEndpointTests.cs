using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>]
internal sealed class ProjectExportEndpointTests(LogicLabWebFactory factory)
{
    [Test]
    public async Task HeadExport_DoesNotConsumeTicketAndReturnsMethodNotAllowed()
    {
        var downloads = new OneTimeDownloads("package-bytes"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            }));
        using var client = host.CreateHttpsClient();
        using var bootstrap = await client.GetAsync(
            new Uri("/editor", UriKind.Relative));
        client.DefaultRequestHeaders.Add("Cookie", AnonymousCookie(bootstrap));
        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            new Uri("/downloads/export-ticket-head-0001", UriKind.Relative));

        using var head = await client.SendAsync(request);
        var headBody = await head.Content.ReadAsByteArrayAsync();
        using var get = await client.GetAsync(
            new Uri("/downloads/export-ticket-head-0001", UriKind.Relative));

        using (Assert.Multiple())
        {
            await Assert.That(head.StatusCode)
                .IsEqualTo(HttpStatusCode.MethodNotAllowed);
            await Assert.That(head.Content.Headers.Allow)
                .IsEquivalentTo([HttpMethod.Get.Method]);
            await Assert.That(head.Content.Headers.ContentType?.MediaType)
                .IsEqualTo("application/problem+json");
            await Assert.That(headBody).IsEmpty();
            await Assert.That(head.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(head.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(downloads.Requests).HasSingleItem();
        }
    }

    [Test]
    public async Task GetExport_FreshAnonymousTicket_StreamsPackageOnceWithPrivateHeaders()
    {
        var downloads = new OneTimeDownloads("package-bytes"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            }));
        using var client = host.CreateHttpsClient();
        using var bootstrap = await client.GetAsync(
            new Uri("/editor", UriKind.Relative));
        client.DefaultRequestHeaders.Add("Cookie", AnonymousCookie(bootstrap));

        using var first = await client.GetAsync(
            new Uri("/downloads/export-ticket-web-0001", UriKind.Relative));
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        using var second = await client.GetAsync(
            new Uri("/downloads/export-ticket-web-0001", UriKind.Relative));

        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(firstBytes).IsEquivalentTo(
                "package-bytes"u8.ToArray(),
                CollectionOrdering.Matching);
            await Assert.That(first.Content.Headers.ContentType?.MediaType)
                .IsEqualTo("application/octet-stream");
            await Assert.That(first.Content.Headers.ContentDisposition?.DispositionType)
                .IsEqualTo("attachment");
            await Assert.That(first.Content.Headers.ContentDisposition?.FileName)
                .IsEqualTo("logiclab-project.logiclab");
            await Assert.That(first.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(first.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(downloads.Requests.Count).IsEqualTo(2);
            await Assert.That(downloads.Requests[0].Caller)
                .IsTypeOf<AnonymousBrowserWorkspaceCaller>();
            await Assert.That(downloads.Requests[1].Caller)
                .IsEqualTo(downloads.Requests[0].Caller);
        }

        await WebTestHttp.AssertProblemDetailsAsync(
            second,
            HttpStatusCode.NotFound,
            "export_expired");
    }

    [Test]
    public async Task GetExport_DifferentAnonymousBrowsers_PassDistinctProtectedCallers()
    {
        var downloads = new AlwaysDownloads("package-bytes"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            }));
        using var clientA = host.CreateHttpsClient();
        using var clientB = host.CreateHttpsClient();
        using var bootstrapA = await clientA.GetAsync(
            new Uri("/editor", UriKind.Relative));
        using var bootstrapB = await clientB.GetAsync(
            new Uri("/editor", UriKind.Relative));
        var cookieA = AnonymousCookie(bootstrapA);
        var cookieB = AnonymousCookie(bootstrapB);
        clientA.DefaultRequestHeaders.Add("Cookie", cookieA);
        clientB.DefaultRequestHeaders.Add("Cookie", cookieB);

        using var responseA = await clientA.GetAsync(
            new Uri("/downloads/export-ticket-browser-a", UriKind.Relative));
        using var responseB = await clientB.GetAsync(
            new Uri("/downloads/export-ticket-browser-b", UriKind.Relative));

        var callerA = (await Assert.That(downloads.Requests[0].Caller)
            .IsTypeOf<AnonymousBrowserWorkspaceCaller>())!;
        var callerB = (await Assert.That(downloads.Requests[1].Caller)
            .IsTypeOf<AnonymousBrowserWorkspaceCaller>())!;
        using (Assert.Multiple())
        {
            await Assert.That(callerA).IsNotEqualTo(callerB);
            await Assert.That(cookieA)
                .StartsWith("__Host-LogicLab.AnonymousCaller=");
            await Assert.That(bootstrapA.Headers.GetValues("Set-Cookie").Single(
                    value => value.StartsWith(
                        $"{AnonymousWorkspaceCallerMiddleware.CookieName}=",
                        StringComparison.Ordinal)))
                .Contains("secure; samesite=lax; httponly", StringComparison.OrdinalIgnoreCase);
            await Assert.That(responseA.Headers.Contains("Set-Cookie")).IsFalse();
        }
    }

    [Test]
    public async Task GetExport_AuthenticatedPrincipal_PassesStableSubjectToRedemption()
    {
        var downloads = new OneTimeDownloads("owner"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            }));
        using var client = host.CreateHttpsClient();

        using var response = await client.GetAsync(
            new Uri("/downloads/export-ticket-owner-0001", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        var caller = (await Assert.That(downloads.Requests.Single().Caller)
            .IsTypeOf<AuthenticatedWorkspaceCaller>())!;
        await Assert.That(caller.SubjectId.Value).IsEqualTo("download-subject");
    }

    [Test]
    public async Task GetExport_TransferLimitsExceeded_RejectBeforeRedemption(
        CancellationToken cancellationToken)
    {
        var downloads = new BlockingDownloads("package-bytes"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "LogicLab:ProjectExports:MaximumConcurrentDownloads",
                "1");
            builder.UseSetting(
                "LogicLab:ProjectExports:DownloadPermitLimit",
                "1");
            builder.ConfigureTestServices(services =>
            {
                ConfigureAuthentication(services);
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            });
        });
        using var client = host.CreateHttpsClient();
        var firstRequest = client.GetAsync(
            new Uri("/downloads/export-ticket-concurrent-01", UriKind.Relative),
            cancellationToken);
        await downloads.Entered.WaitAsync(cancellationToken);

        try
        {
            using var rejected = await client.GetAsync(
                new Uri("/downloads/export-ticket-concurrent-02", UriKind.Relative),
                cancellationToken);

            await WebTestHttp.AssertProblemDetailsAsync(
                rejected,
                HttpStatusCode.TooManyRequests,
                "export_download_rate_limit_exceeded");
            using (Assert.Multiple())
            {
                await Assert.That(downloads.RequestCount).IsEqualTo(1);
                await Assert.That(rejected.Headers.CacheControl?.Private).IsTrue();
                await Assert.That(rejected.Headers.CacheControl?.NoStore).IsTrue();
            }
        }
        finally
        {
            downloads.Release();
        }

        using var first = await firstRequest;
        first.EnsureSuccessStatusCode();

        using var rateRejected = await client.GetAsync(
            new Uri("/downloads/export-ticket-rate-limit-01", UriKind.Relative),
            cancellationToken);
        await WebTestHttp.AssertProblemDetailsAsync(
            rateRejected,
            HttpStatusCode.TooManyRequests,
            "export_download_rate_limit_exceeded");
        using (Assert.Multiple())
        {
            await Assert.That(downloads.RequestCount).IsEqualTo(1);
            await Assert.That(rateRejected.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(rateRejected.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task GetExport_MalformedTicket_ConcealsWithoutStoreAccess()
    {
        var downloads = new OneTimeDownloads("unused"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            }));
        using var client = host.CreateHttpsClient();

        using var response = await client.GetAsync(
            new Uri("/downloads/INVALID!", UriKind.Relative));

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.NotFound,
            "export_expired");
        using (Assert.Multiple())
        {
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(downloads.Requests).IsEmpty();
        }
    }

    [Test]
    [Arguments("POST")]
    [Arguments("PUT")]
    [Arguments("DELETE")]
    public async Task UnsupportedMethod_ReturnsContractProblemWithoutRedemption(
        string method)
    {
        var downloads = new OneTimeDownloads("unused"u8.ToArray());
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProjectExportDownloads>();
                services.AddSingleton<IProjectExportDownloads>(downloads);
            }));
        using var client = host.CreateHttpsClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            new Uri("/downloads/export-ticket-method-0001", UriKind.Relative));

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.MethodNotAllowed,
            "export_download_method_not_allowed");
        using (Assert.Multiple())
        {
            await Assert.That(response.Content.Headers.Allow)
                .IsEquivalentTo([HttpMethod.Get.Method]);
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(downloads.Requests).IsEmpty();
        }
    }

    private static string AnonymousCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(
            $"{AnonymousWorkspaceCallerMiddleware.CookieName}=",
            StringComparison.Ordinal)).Split(';', 2)[0];

    private static void ConfigureAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    DownloadAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme =
                    DownloadAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, DownloadAuthenticationHandler>(
                DownloadAuthenticationHandler.SchemeName,
                configureOptions: null);
    }

    private sealed class OneTimeDownloads(byte[] bytes) : IProjectExportDownloads
    {
        private int redemptionCount;

        public List<ProjectExportDownloadRequest> Requests { get; } = [];

        public ValueTask<ProjectExportDownloadOutcome> RedeemAsync(
            ProjectExportDownloadRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult<ProjectExportDownloadOutcome>(
                Interlocked.Increment(ref redemptionCount) == 1
                    ? new ProjectExportDownloaded(
                        new MemoryStream(bytes, writable: false),
                        checked((ulong)bytes.Length))
                    : new ProjectExportDownloadRejected("export_expired"));
        }
    }

    private sealed class AlwaysDownloads(byte[] bytes) : IProjectExportDownloads
    {
        public List<ProjectExportDownloadRequest> Requests { get; } = [];

        public ValueTask<ProjectExportDownloadOutcome> RedeemAsync(
            ProjectExportDownloadRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult<ProjectExportDownloadOutcome>(
                new ProjectExportDownloaded(
                    new MemoryStream(bytes, writable: false),
                    checked((ulong)bytes.Length)));
        }
    }

    private sealed class BlockingDownloads(byte[] bytes) : IProjectExportDownloads
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int requestCount;

        public Task Entered => entered.Task;

        public int RequestCount => Volatile.Read(ref requestCount);

        public async ValueTask<ProjectExportDownloadOutcome> RedeemAsync(
            ProjectExportDownloadRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new ProjectExportDownloaded(
                new MemoryStream(bytes, writable: false),
                checked((ulong)bytes.Length));
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class DownloadAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            loggerFactory,
            encoder)
    {
        public const string SchemeName = "ProjectExportEndpointTests";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "download-subject")],
                SchemeName);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
