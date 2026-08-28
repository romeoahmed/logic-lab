using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace LogicLab.Web.BrowserTests;

internal sealed class LogicLabBrowserApplication : TestWebApplicationFactory<Program>
{
    private readonly X509Certificate2 certificate = CreateCertificate();

    public LogicLabBrowserApplication() => UseKestrel(options =>
        options.Listen(
            IPAddress.Loopback,
            0,
            endpoint => endpoint.UseHttps(certificate)));

    public Uri EditorUri
    {
        get
        {
            StartServer();
            return new Uri(ClientOptions.BaseAddress, "editor");
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:LogicLab", "Data Source=:memory:");
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
