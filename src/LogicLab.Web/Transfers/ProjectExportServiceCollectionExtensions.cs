using Microsoft.Extensions.Options;

namespace LogicLab.Web.Transfers;

internal static class ProjectExportServiceCollectionExtensions
{
    public static IServiceCollection AddProjectExportPolicies(this IServiceCollection services)
    {
        services.AddOptions<ProjectExportOptions>()
            .BindConfiguration(
                ProjectExportOptions.ConfigurationSectionName,
                static binder => binder.ErrorOnUnknownConfiguration = true)
            .Validate(
                static options => options.IsValid(),
                "Project export limits and durations must be positive.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider
            .GetRequiredService<IOptions<ProjectExportOptions>>()
            .Value.CreateTransferPolicy());
        services.AddSingleton(provider => provider
            .GetRequiredService<IOptions<ProjectExportOptions>>()
            .Value.CreateStoragePolicy());
        services.AddSingleton(provider => provider
            .GetRequiredService<IOptions<ProjectExportOptions>>()
            .Value.CreatePreparationPolicy());
        return services;
    }
}
