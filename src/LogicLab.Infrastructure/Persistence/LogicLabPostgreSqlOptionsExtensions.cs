using LogicLab.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LogicLab.Infrastructure.Persistence;

public static class LogicLabPostgreSqlOptionsExtensions
{
    public const string MigrationsSchema = "migrations";

    private const string IdentityHistoryTable = "identity";
    private const string PersistenceHistoryTable = "persistence";

    public static DbContextOptionsBuilder UseLogicLabIdentityPostgreSql(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        Configure(options, dataSource, IdentityHistoryTable);
        return options;
    }

    public static DbContextOptionsBuilder<ApplicationIdentityDbContext>
        UseLogicLabIdentityPostgreSql(
            this DbContextOptionsBuilder<ApplicationIdentityDbContext> options,
            NpgsqlDataSource dataSource)
    {
        Configure(options, dataSource, IdentityHistoryTable);
        return options;
    }

    public static DbContextOptionsBuilder UseLogicLabPersistencePostgreSql(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        Configure(options, dataSource, PersistenceHistoryTable);
        return options;
    }

    public static DbContextOptionsBuilder<LogicLabDbContext>
        UseLogicLabPersistencePostgreSql(
            this DbContextOptionsBuilder<LogicLabDbContext> options,
            NpgsqlDataSource dataSource)
    {
        Configure(options, dataSource, PersistenceHistoryTable);
        return options;
    }

    private static void Configure(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource,
        string historyTable)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);

        options.UseNpgsql(
            dataSource,
            postgres => postgres.MigrationsHistoryTable(
                historyTable,
                MigrationsSchema));
    }
}
