using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace LogicLab.Web.Testing;

internal sealed class LogicLabKestrelApplication : TestWebApplicationFactory<Program>, IAsyncInitializer
{
    private readonly X509Certificate2 certificate = CreateCertificate();

    public LogicLabKestrelApplication() => UseKestrel(options =>
        options.Listen(
            IPAddress.Loopback,
            0,
            endpoint => endpoint.UseHttps(certificate)));

    public Task InitializeAsync()
    {
        StartServer();
        return Task.CompletedTask;
    }

    public Uri EditorUri => new(ClientOptions.BaseAddress, "editor");

    public HttpClient CreateHttpsClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented?.Thumbprint == certificate.Thumbprint,
        })
        {
            BaseAddress = ClientOptions.BaseAddress,
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(
            "ConnectionStrings:LogicLab",
            "Host=localhost;Port=5432;Database=logiclab_browser_tests;Username=logiclab");
        // Each browser context is a distinct anonymous caller. Ingress exhaustion
        // belongs to WebHostSecurityTests, not to concurrent interface scenarios.
        builder.ConfigureTestServices(services => services.Replace(
            ServiceDescriptor.Singleton(new AnonymousWorkspaceIngressPolicy(
                issuancePermitLimit: 128,
                issuanceWindow: TimeSpan.FromMinutes(1)))));
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                certificate.Dispose();
            }
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2_048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature,
            critical: true));

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(1));
    }
}
