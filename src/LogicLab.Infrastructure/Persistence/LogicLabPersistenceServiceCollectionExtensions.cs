using LogicLab.Application.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Infrastructure.Persistence;

public static class LogicLabPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLogicLabSqlitePersistence(
        this IServiceCollection services,
        string connectionString,
        int durableCommandReceiptCount)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durableCommandReceiptCount);

        services.AddDbContextFactory<LogicLabDbContext>(options =>
            options.UseSqlite(connectionString));
        services.AddSingleton(provider =>
            new SqliteDurableProjectRepository(
                provider.GetRequiredService<IDbContextFactory<LogicLabDbContext>>(),
                durableCommandReceiptCount));
        services.AddSingleton<IDurableProjectRepository>(provider =>
            provider.GetRequiredService<SqliteDurableProjectRepository>());
        services.AddSingleton<IDurableProjectCatalogRepository>(provider =>
            provider.GetRequiredService<SqliteDurableProjectRepository>());
        services.AddSingleton<IDurableProjectLoader>(provider =>
            provider.GetRequiredService<SqliteDurableProjectRepository>());
        services.AddSingleton(provider =>
            new LogicLabPersistenceReadiness(
                provider.GetRequiredService<IDbContextFactory<LogicLabDbContext>>()));
        return services;
    }
}
