using Microsoft.EntityFrameworkCore;

namespace LogicLab.Infrastructure.Persistence;

public interface ILogicLabPersistenceReadiness
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

internal sealed class LogicLabPersistenceReadiness(
    IDbContextFactory<LogicLabDbContext> contextFactory)
    : ILogicLabPersistenceReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(
            cancellationToken);
        return await context.Database.CanConnectAsync(cancellationToken)
            && !(await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .Any();
    }
}
