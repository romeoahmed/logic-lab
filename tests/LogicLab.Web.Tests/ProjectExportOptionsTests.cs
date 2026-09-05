using LogicLab.Web.Transfers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

internal sealed class ProjectExportOptionsTests
{
    [Test]
    public async Task Start_InvalidProjectExportConfiguration_FailsOptionsValidation(
        CancellationToken cancellationToken)
    {
        // A direct Host preserves startup failures before disposal by the web test entry point.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
        });
        builder.Configuration["LogicLab:ProjectExports:MaximumPublishedExports"] = "0";
        builder.Services.AddProjectExportPolicies();
        using var host = builder.Build();

        await Assert.That(() => host.StartAsync(cancellationToken))
            .ThrowsExactly<OptionsValidationException>();
    }
}
