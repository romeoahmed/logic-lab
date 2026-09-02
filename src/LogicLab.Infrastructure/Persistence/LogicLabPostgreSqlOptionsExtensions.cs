using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LogicLab.Infrastructure.Persistence;

public static class LogicLabPostgreSqlOptionsExtensions
{
    public const string MigrationsSchema = "migrations";

    private const string PersistenceHistoryTable = "persistence";

    internal static DbContextOptionsBuilder UseLogicLabPersistencePostgreSql(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        Configure(options, dataSource);
        return options;
    }

    public static DbContextOptionsBuilder<LogicLabDbContext>
        UseLogicLabPersistencePostgreSql(
            this DbContextOptionsBuilder<LogicLabDbContext> options,
            NpgsqlDataSource dataSource)
    {
        Configure(options, dataSource);
        return options;
    }

    private static void Configure(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);

        options.UseNpgsql(
            dataSource,
            postgres => postgres.MigrationsHistoryTable(
                PersistenceHistoryTable,
                MigrationsSchema));
    }
}
