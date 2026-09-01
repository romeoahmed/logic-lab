using Azure.Core;
using Azure.Identity;
using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

const string ConnectionStringEnvironmentVariable = "ConnectionStrings__LogicLab";
const string ManagedIdentityClientIdEnvironmentVariable = "AZURE_CLIENT_ID";

var connectionString = Environment.GetEnvironmentVariable(
    ConnectionStringEnvironmentVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"Environment variable {ConnectionStringEnvironmentVariable} is required.");
}

var managedIdentityClientId = Environment.GetEnvironmentVariable(
    ManagedIdentityClientIdEnvironmentVariable);
if (!Guid.TryParse(managedIdentityClientId, out var clientId))
{
    throw new InvalidOperationException(
        $"Environment variable {ManagedIdentityClientIdEnvironmentVariable} must be a GUID.");
}

var database = new NpgsqlConnectionStringBuilder(connectionString);
if (!string.IsNullOrEmpty(database.Password)
    || string.IsNullOrWhiteSpace(database.Username)
    || database.SslMode != SslMode.VerifyFull)
{
    throw new InvalidOperationException(
        "The migration connection must be passwordless and use SSL Mode=VerifyFull.");
}

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
ConfigureAzurePostgreSqlAuthentication(
    dataSourceBuilder,
    new ManagedIdentityCredential(
        ManagedIdentityId.FromUserAssignedClientId(clientId.ToString())));

await using var dataSource = dataSourceBuilder.Build();
await using (var identityContext = new ApplicationIdentityDbContext(
    new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
        .UseNpgsql(dataSource)
        .Options))
{
    await MigrateAsync(
        identityContext,
        "identity",
        cancellationSource.Token).ConfigureAwait(false);
}

await using (var logicLabContext = new LogicLabDbContext(
    new DbContextOptionsBuilder<LogicLabDbContext>()
        .UseNpgsql(dataSource)
        .Options))
{
    await MigrateAsync(
        logicLabContext,
        "logic-lab",
        cancellationSource.Token).ConfigureAwait(false);
}

Console.WriteLine("Database migrations completed.");

static void ConfigureAzurePostgreSqlAuthentication(
    NpgsqlDataSourceBuilder dataSourceBuilder,
    TokenCredential credential)
{
    var tokenRequest = new TokenRequestContext(
        ["https://ossrdbms-aad.database.windows.net/.default"]);
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, cancellationToken) =>
            (await credential.GetTokenAsync(tokenRequest, cancellationToken)).Token,
        TimeSpan.FromMinutes(55),
        TimeSpan.FromSeconds(5));
}

static async Task MigrateAsync(
    DbContext context,
    string migrationSet,
    CancellationToken cancellationToken)
{
    var pendingMigrations = await context.Database
        .GetPendingMigrationsAsync(cancellationToken)
        .ConfigureAwait(false);
    var pendingMigrationCount = pendingMigrations.Count();

    Console.WriteLine(
        $"Applying {pendingMigrationCount} {migrationSet} migration(s).");
    await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

    var remainingMigrations = await context.Database
        .GetPendingMigrationsAsync(cancellationToken)
        .ConfigureAwait(false);
    if (remainingMigrations.Any())
    {
        throw new InvalidOperationException(
            $"The {migrationSet} migration set is incomplete.");
    }
}
