using Microsoft.EntityFrameworkCore;

namespace LogicLab.Infrastructure.Persistence;

public sealed class LogicLabPersistenceReadiness
{
    private readonly IDbContextFactory<LogicLabDbContext> contextFactory;

    internal LogicLabPersistenceReadiness(
        IDbContextFactory<LogicLabDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        if (!await context.Database.CanConnectAsync(cancellationToken))
        {
            return false;
        }

        var expectedMigrations = context.Database.GetMigrations();
        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync(cancellationToken);
        return expectedMigrations.SequenceEqual(appliedMigrations);
    }
}
