using LogicLab.Application.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Infrastructure.Persistence;

public static class LogicLabPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLogicLabSqlitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        services.AddDbContextFactory<LogicLabDbContext>(options =>
            options.UseSqlite(connectionString));
        services.AddSingleton<IDurableProjectRepository>(provider =>
            new SqliteDurableProjectRepository(
                provider.GetRequiredService<IDbContextFactory<LogicLabDbContext>>()));
        return services;
    }
}
