using Azure.Core;
using Azure.Identity;
using LogicLab.DatabaseMigrator;
using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

var credential = new ManagedIdentityCredential(
    ManagedIdentityId.FromUserAssignedClientId(clientId.ToString()));

if (args is ["bootstrap"])
{
    var configuration = DatabaseBootstrapConfiguration.Load();
    var databaseConnection = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Database = configuration.DatabaseName,
    };
    await using var administratorDataSource = CreateDataSource(
        connectionString,
        credential);
    await using var databaseDataSource = CreateDataSource(
        databaseConnection.ConnectionString,
        credential);
    await DatabaseBootstrapper.RunAsync(
        administratorDataSource,
        databaseDataSource,
        configuration,
        cancellationSource.Token).ConfigureAwait(false);
    Console.WriteLine("Database principals and grants are current.");
    return;
}

if (args.Length != 0)
{
    throw new InvalidOperationException(
        "The only supported operation argument is bootstrap.");
}

await using var dataSource = CreateDataSource(connectionString, credential);
var identityHostBuilder = Host.CreateApplicationBuilder();
identityHostBuilder.Services.AddLogicLabIdentity(dataSource);
using var identityHost = identityHostBuilder.Build();
await using (var identityScope = identityHost.Services.CreateAsyncScope())
{
    var identityContext = identityScope.ServiceProvider
        .GetRequiredService<ApplicationIdentityDbContext>();
    await MigrateAsync(
        identityContext,
        "identity",
        cancellationSource.Token).ConfigureAwait(false);
}

await using (var logicLabContext = new LogicLabDbContext(
    new DbContextOptionsBuilder<LogicLabDbContext>()
        .UseLogicLabPersistencePostgreSql(dataSource)
        .Options))
{
    await MigrateAsync(
        logicLabContext,
        "logic-lab",
        cancellationSource.Token).ConfigureAwait(false);
}

Console.WriteLine("Database migrations completed.");

static NpgsqlDataSource CreateDataSource(
    string connectionString,
    TokenCredential credential)
{
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    var tokenRequest = new TokenRequestContext(
        ["https://ossrdbms-aad.database.windows.net/.default"]);
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, cancellationToken) =>
            (await credential.GetTokenAsync(tokenRequest, cancellationToken)).Token,
        TimeSpan.FromMinutes(55),
        TimeSpan.FromSeconds(5));
    return dataSourceBuilder.Build();
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
