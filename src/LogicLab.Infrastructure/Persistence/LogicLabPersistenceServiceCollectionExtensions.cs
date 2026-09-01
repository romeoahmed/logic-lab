using LogicLab.Application.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LogicLab.Infrastructure.Persistence;

public static class LogicLabPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLogicLabPersistence(
        this IServiceCollection services,
        NpgsqlDataSource dataSource,
        int durableCommandReceiptCount)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durableCommandReceiptCount);

        services.AddDbContextFactory<LogicLabDbContext>(options =>
            options.UseNpgsql(dataSource));
        services.AddSingleton(provider =>
            new DurableProjectRepository(
                provider.GetRequiredService<IDbContextFactory<LogicLabDbContext>>(),
                durableCommandReceiptCount));
        services.AddSingleton<IDurableProjectRepository>(provider =>
            provider.GetRequiredService<DurableProjectRepository>());
        services.AddSingleton<IDurableProjectCatalogRepository>(provider =>
            provider.GetRequiredService<DurableProjectRepository>());
        services.AddSingleton<IDurableProjectLoader>(provider =>
            provider.GetRequiredService<DurableProjectRepository>());
        services.AddSingleton(provider =>
            new LogicLabPersistenceReadiness(
                provider.GetRequiredService<IDbContextFactory<LogicLabDbContext>>()));
        return services;
    }
}
