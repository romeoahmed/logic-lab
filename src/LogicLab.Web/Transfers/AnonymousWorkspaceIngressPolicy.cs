using System.Threading.RateLimiting;

namespace LogicLab.Web.Transfers;

internal sealed record AnonymousWorkspaceIngressPolicy
{
    public AnonymousWorkspaceIngressPolicy(
        int issuancePermitLimit,
        TimeSpan issuanceWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issuancePermitLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            issuanceWindow,
            TimeSpan.Zero);

        IssuancePermitLimit = issuancePermitLimit;
        IssuanceWindow = issuanceWindow;
    }

    public int IssuancePermitLimit { get; }

    public TimeSpan IssuanceWindow { get; }

    public static AnonymousWorkspaceIngressPolicy Default { get; } = new(
        issuancePermitLimit: 8,
        issuanceWindow: TimeSpan.FromMinutes(1));
}

internal sealed class AnonymousWorkspaceIngressLimiter : IAsyncDisposable
{
    private readonly FixedWindowRateLimiter limiter;

    public AnonymousWorkspaceIngressLimiter(AnonymousWorkspaceIngressPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        limiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = policy.IssuancePermitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = policy.IssuanceWindow,
            });
    }

    public RateLimitLease AttemptAcquire() => limiter.AttemptAcquire();

    public ValueTask DisposeAsync() => limiter.DisposeAsync();
}
