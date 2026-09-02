using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace LogicLab.Web.Tests;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class RequiresPostgreSqlAttribute()
    : SkipAttribute(
        $"Set {PostgreSqlIdentityTestDatabase.ConnectionStringEnvironmentVariable} "
        + "to run PostgreSQL integration tests.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(
                PostgreSqlIdentityTestDatabase.ConnectionStringEnvironmentVariable)));
    }
}

internal sealed class PostgreSqlIdentityTestDatabase : IAsyncDisposable
{
    public const string ConnectionStringEnvironmentVariable =
        "LOGICLAB_TEST_POSTGRES_CONNECTION_STRING";

    private string? administrativeConnectionString;
    private string? databaseName;
    private NpgsqlDataSource? dataSource;

    public NpgsqlDataSource DataSource => dataSource
        ?? throw new InvalidOperationException(
            "The PostgreSQL test database is not available.");

    public static async Task<PostgreSqlIdentityTestDatabase> CreateAsync()
    {
        var database = new PostgreSqlIdentityTestDatabase();
        try
        {
            await database.InitializeAsync().ConfigureAwait(false);
            return database;
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (dataSource is null)
        {
            return;
        }

        await dataSource.DisposeAsync().ConfigureAwait(false);
        dataSource = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        var name = databaseName;
        databaseName = null;
        if (name is null || administrativeConnectionString is null)
        {
            return;
        }

        try
        {
            await ExecuteAdministrativeCommandAsync(
                $"DROP DATABASE IF EXISTS {name} WITH (FORCE)")
                .ConfigureAwait(false);
        }
        finally
        {
            administrativeConnectionString = null;
        }
    }

    private async Task InitializeAsync()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} is required.");
        var administrative = new NpgsqlConnectionStringBuilder(
            configuredConnectionString)
        {
            Database = "postgres",
        };
        administrativeConnectionString = administrative.ConnectionString;

        var name = $"logiclab_identity_test_{Guid.CreateVersion7():N}";
        await ExecuteAdministrativeCommandAsync($"CREATE DATABASE {name}")
            .ConfigureAwait(false);
        databaseName = name;

        var database = new NpgsqlConnectionStringBuilder(
            configuredConnectionString)
        {
            Database = name,
        };
        dataSource = NpgsqlDataSource.Create(database.ConnectionString);

        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services.AddLogicLabIdentity(dataSource);
        using var host = hostBuilder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ApplicationIdentityDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema}")
            .ConfigureAwait(false);
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }

    private async Task ExecuteAdministrativeCommandAsync(string commandText)
    {
        await using var connection = new NpgsqlConnection(
            administrativeConnectionString
            ?? throw new InvalidOperationException(
                "The administrative PostgreSQL connection has not been configured."));
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
