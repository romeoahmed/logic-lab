using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LogicLab.Web.Health;

internal sealed class LogicLabReadinessHealthCheck(
    IEditorWorkspaceReadiness workspace,
    LogicLabPersistenceReadiness persistence,
    DataProtectionReadiness dataProtection,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (lifetime.ApplicationStopping.IsCancellationRequested || !workspace.IsReady)
        {
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            if (!await persistence.IsReadyAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy();
            }

            if (!await dataProtection.IsReadyAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy();
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var identity = scope.ServiceProvider
                .GetRequiredService<ApplicationIdentityDbContext>();
            var identityReady = await identity.Database.CanConnectAsync(cancellationToken)
                && identity.Database.GetMigrations().SequenceEqual(
                    await identity.Database.GetAppliedMigrationsAsync(
                        cancellationToken));
            return identityReady
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
