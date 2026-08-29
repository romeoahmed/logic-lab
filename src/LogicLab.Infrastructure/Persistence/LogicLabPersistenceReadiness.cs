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
        return await context.Database.CanConnectAsync(cancellationToken)
            && !(await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .Any();
    }
}
