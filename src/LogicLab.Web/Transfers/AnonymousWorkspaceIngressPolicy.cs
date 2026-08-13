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

    public RateLimiter CreateLimiter() =>
        IngressRateLimiting.FixedWindow(IssuancePermitLimit, IssuanceWindow);
}
