using LogicLab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LogicLab.Infrastructure.Identity;

public static class LogicLabIdentityServiceCollectionExtensions
{
    private const string MigrationsHistoryTable = "identity";

    public static IdentityBuilder AddLogicLabIdentity(
        this IServiceCollection services,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        services.AddDbContext<ApplicationIdentityDbContext>(options =>
            options.UseNpgsql(
                dataSource,
                postgres => postgres.MigrationsHistoryTable(
                    MigrationsHistoryTable,
                    LogicLabPostgreSqlOptionsExtensions.MigrationsSchema)));
        return services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddEntityFrameworkStores<ApplicationIdentityDbContext>();
    }
}
