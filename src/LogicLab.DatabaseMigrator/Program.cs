using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

const string ConnectionStringEnvironmentVariable = "ConnectionStrings__LogicLab";

var connectionString = Environment.GetEnvironmentVariable(
    ConnectionStringEnvironmentVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"Environment variable {ConnectionStringEnvironmentVariable} is required.");
}

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

await using (var identityContext = new ApplicationIdentityDbContext(
    new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
        .UseNpgsql(connectionString)
        .Options))
{
    await MigrateAsync(
        identityContext,
        "identity",
        cancellationSource.Token).ConfigureAwait(false);
}

await using (var logicLabContext = new LogicLabDbContext(
    new DbContextOptionsBuilder<LogicLabDbContext>()
        .UseNpgsql(connectionString)
        .Options))
{
    await MigrateAsync(
        logicLabContext,
        "logic-lab",
        cancellationSource.Token).ConfigureAwait(false);
}

Console.WriteLine("Database migrations completed.");

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
