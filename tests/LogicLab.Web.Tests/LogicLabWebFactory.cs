using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using TUnit.AspNetCore;

namespace LogicLab.Web.Tests;

internal sealed class LogicLabWebFactory : TestWebApplicationFactory<Program>
{
    public LogicLabWebFactory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost/");
        ClientOptions.AllowAutoRedirect = false;
        ClientOptions.HandleCookies = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(Environments.Staging);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        // TUnit's parameterless CreateClient uses this hook instead of ClientOptions.
        client.BaseAddress = ClientOptions.BaseAddress;
    }
}
