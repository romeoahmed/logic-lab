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
        NpgsqlDataSource dataSource,
        DatabaseBootstrapConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                connection.Database,
                "postgres",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The bootstrap connection must target the postgres database.");
        }

        await EnsurePrincipalAsync(
            connection,
            configuration.WebPrincipalName,
            configuration.WebPrincipalObjectId,
            cancellationToken).ConfigureAwait(false);
        await EnsurePrincipalAsync(
            connection,
            configuration.MigratorPrincipalName,
            configuration.MigratorPrincipalObjectId,
            cancellationToken).ConfigureAwait(false);

        await connection.ChangeDatabaseAsync(
            configuration.DatabaseName,
            cancellationToken).ConfigureAwait(false);
        await ApplyGrantsAsync(
            connection,
            configuration,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsurePrincipalAsync(
        NpgsqlConnection connection,
        string principalName,
        Guid principalObjectId,
        CancellationToken cancellationToken)
    {
        const string PrincipalSql =
            "SELECT * FROM pg_catalog.pgaadauth_list_principals(false) WHERE rolname::text = $1;";
        await using (var principalCommand = new NpgsqlCommand(
            PrincipalSql,
            connection))
        {
            principalCommand.Parameters.AddWithValue(principalName);
            await using var reader = await principalCommand
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                && string.Equals(reader.GetString(1), "service", StringComparison.Ordinal)
                && Guid.TryParse(reader.GetString(2), out var currentObjectId)
                && currentObjectId == principalObjectId
                && reader.GetInt32(4) == 0
                && reader.GetInt32(5) == 0)
            {
                return;
            }
        }

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
            ALTER ROLE {web} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
            ALTER ROLE {migrator} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
