using LogicLab.Infrastructure.Persistence;
using Npgsql;

namespace LogicLab.DatabaseMigrator;

internal sealed record DatabaseBootstrapConfiguration(
    string DatabaseName,
    string WebPrincipalName,
    Guid WebPrincipalObjectId,
    string MigratorPrincipalName,
    Guid MigratorPrincipalObjectId)
{
    public static DatabaseBootstrapConfiguration Load()
    {
        return new DatabaseBootstrapConfiguration(
            ReadRequired("Database__Name"),
            ReadRequired("Database__WebPrincipalName"),
            ReadGuid("Database__WebPrincipalObjectId"),
            ReadRequired("Database__MigratorPrincipalName"),
            ReadGuid("Database__MigratorPrincipalObjectId"));
    }

    private static Guid ReadGuid(string name)
    {
        var value = ReadRequired(name);
        return Guid.TryParse(value, out var result)
            ? result
            : throw new InvalidOperationException(
                $"Environment variable {name} must be a GUID.");
    }

    private static string ReadRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Environment variable {name} is required.");
    }
}

internal static class DatabaseBootstrapper
{
    public static async Task RunAsync(
        NpgsqlDataSource administratorDataSource,
        NpgsqlDataSource databaseDataSource,
        DatabaseBootstrapConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administratorDataSource);
        ArgumentNullException.ThrowIfNull(databaseDataSource);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var administratorConnection = await administratorDataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                administratorConnection.Database,
                "postgres",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The bootstrap connection must target the postgres database.");
        }

        await EnsurePrincipalAsync(
            administratorConnection,
            configuration.WebPrincipalName,
            configuration.WebPrincipalObjectId,
            cancellationToken).ConfigureAwait(false);
        await EnsurePrincipalAsync(
            administratorConnection,
            configuration.MigratorPrincipalName,
            configuration.MigratorPrincipalObjectId,
            cancellationToken).ConfigureAwait(false);

        await using var databaseConnection = await databaseDataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                databaseConnection.Database,
                configuration.DatabaseName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The database connection must target the configured database.");
        }

        await ApplyGrantsAsync(
            databaseConnection,
            configuration,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsurePrincipalAsync(
        NpgsqlConnection connection,
        string principalName,
        Guid principalObjectId,
        CancellationToken cancellationToken)
    {
        const string RoleExistsSql =
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = $1);";
        await using var existsCommand = new NpgsqlCommand(RoleExistsSql, connection);
        existsCommand.Parameters.AddWithValue(principalName);
        var exists = (bool)(await existsCommand
            .ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        if (!exists)
        {
            const string CreatePrincipalSql =
                "SELECT * FROM pg_catalog.pgaadauth_create_principal_with_oid($1, $2, 'service', false, false);";
            await using var createCommand = new NpgsqlCommand(
                CreatePrincipalSql,
                connection);
            createCommand.Parameters.AddWithValue(principalName);
            createCommand.Parameters.AddWithValue(principalObjectId.ToString());
            await createCommand.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        using var commandBuilder = new NpgsqlCommandBuilder();
        var role = commandBuilder.QuoteIdentifier(principalName);
        await using var updateCommand = new NpgsqlCommand(
            $"SECURITY LABEL FOR \"pgaadauth\" ON ROLE {role} IS 'aadauth,oid={principalObjectId:D},type=service';",
            connection);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplyGrantsAsync(
        NpgsqlConnection connection,
        DatabaseBootstrapConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var commandBuilder = new NpgsqlCommandBuilder();
        var database = commandBuilder.QuoteIdentifier(configuration.DatabaseName);
        var web = commandBuilder.QuoteIdentifier(configuration.WebPrincipalName);
        var migrator = commandBuilder.QuoteIdentifier(
            configuration.MigratorPrincipalName);
        var sql = $"""
            GRANT CONNECT ON DATABASE {database} TO CURRENT_USER;
            REVOKE ALL ON DATABASE {database} FROM PUBLIC;
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            CREATE SCHEMA IF NOT EXISTS {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema};
            REVOKE ALL ON SCHEMA {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {database} TO {web};
            GRANT USAGE ON SCHEMA public TO {web};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {web};
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {web};
            GRANT USAGE ON SCHEMA {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema} TO {web};
            GRANT SELECT ON ALL TABLES IN SCHEMA {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema} TO {web};
            GRANT CONNECT ON DATABASE {database} TO {migrator};
            GRANT USAGE, CREATE ON SCHEMA public TO {migrator};
            GRANT USAGE, CREATE ON SCHEMA {LogicLabPostgreSqlOptionsExtensions.MigrationsSchema} TO {migrator};
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
