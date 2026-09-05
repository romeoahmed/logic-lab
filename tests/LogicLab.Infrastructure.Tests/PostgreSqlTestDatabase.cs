using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace LogicLab.Infrastructure.Tests;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequiresPostgreSqlAttribute()
    : SkipAttribute(
        $"Set {PostgreSqlTestDatabase.ConnectionStringEnvironmentVariable} to run PostgreSQL integration tests.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(
                PostgreSqlTestDatabase.ConnectionStringEnvironmentVariable)));
    }
}

internal sealed class PostgreSqlTestDatabase : IAsyncInitializer, IAsyncDisposable
{
    public const string ConnectionStringEnvironmentVariable =
        "LOGICLAB_TEST_POSTGRES_CONNECTION_STRING";

    private string? administrativeConnectionString;
    private string? databaseName;
    private NpgsqlDataSource? dataSource;
    private DbContextOptions<LogicLabDbContext>? options;

    public async Task InitializeAsync()
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
        var name = $"logiclab_test_{Guid.CreateVersion7():N}";

        try
        {
            await ExecuteAdministrativeCommandAsync(
                $"CREATE DATABASE {name}").ConfigureAwait(false);
            databaseName = name;

            var database = new NpgsqlConnectionStringBuilder(
                configuredConnectionString)
            {
                Database = name,
            };
            dataSource = NpgsqlDataSource.Create(database.ConnectionString);
            options = new DbContextOptionsBuilder<LogicLabDbContext>()
                .UseLogicLabPersistencePostgreSql(dataSource)
                .Options;

            await using var context = CreateContext();
            await context.Database.ExecuteSqlRawAsync(
                $"CREATE SCHEMA {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema}")
                .ConfigureAwait(false);
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public DurableProjectRepository CreateRepository(
        int receiptRetentionCount = 1_024,
        params IInterceptor[] interceptors)
    {
        var configuredOptions = options ?? throw new InvalidOperationException(
            "The PostgreSQL test database has not been initialized.");
        return new DurableProjectRepository(
            new TestDbContextFactory(new DbContextOptionsBuilder<LogicLabDbContext>(
                    configuredOptions)
                .AddInterceptors(interceptors)
                .Options),
            receiptRetentionCount);
    }

    public LogicLabDbContext CreateContext() => new(
        options ?? throw new InvalidOperationException(
            "The PostgreSQL test database has not been initialized."));

    public async ValueTask DisposeAsync()
    {
        options = null;
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync().ConfigureAwait(false);
            dataSource = null;
        }

        var name = databaseName;
        databaseName = null;
        if (name is not null && administrativeConnectionString is not null)
        {
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

    private sealed class TestDbContextFactory(
        DbContextOptions<LogicLabDbContext> options)
        : IDbContextFactory<LogicLabDbContext>
    {
        public LogicLabDbContext CreateDbContext() => new(options);

        public Task<LogicLabDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
