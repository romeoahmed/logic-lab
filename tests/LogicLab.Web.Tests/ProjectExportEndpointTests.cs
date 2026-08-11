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
        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            new Uri("/downloads/export-ticket-head-0001", UriKind.Relative));

        using var head = await client.SendAsync(request);
        using var get = await client.GetAsync(
            new Uri("/downloads/export-ticket-head-0001", UriKind.Relative));

        using (Assert.Multiple())
        {
            await Assert.That(head.StatusCode)
                .IsEqualTo(HttpStatusCode.MethodNotAllowed);
            await Assert.That(head.Content.Headers.Allow)
                .IsEquivalentTo([HttpMethod.Get.Method]);
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

        using var first = await client.GetAsync(
            new Uri("/downloads/export-ticket-web-0001", UriKind.Relative));
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        using var second = await client.GetAsync(
            new Uri("/downloads/export-ticket-web-0001", UriKind.Relative));

        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(firstBytes).IsEquivalentTo("package-bytes"u8.ToArray());
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
                .IsEqualTo(AnonymousWorkspaceCaller.Instance);
        }

        await WebTestHttp.AssertProblemDetailsAsync(
            second,
            HttpStatusCode.NotFound,
            "export_expired");
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
        await Assert.That(downloads.Requests).IsEmpty();
    }

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
