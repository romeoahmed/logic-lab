using System.Security.Claims;
using System.Threading.RateLimiting;

namespace LogicLab.Web.Projects;

internal sealed record DurableProjectIngressPolicy
{
    public const string OpenRateLimitPolicyName = "project-open";

    public DurableProjectIngressPolicy(
        int openPermitLimit,
        TimeSpan openWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(openPermitLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            openWindow,
            TimeSpan.Zero);

        OpenPermitLimit = openPermitLimit;
        OpenWindow = openWindow;
    }

    public int OpenPermitLimit { get; }

    public TimeSpan OpenWindow { get; }

    public static DurableProjectIngressPolicy Default { get; } = new(
        openPermitLimit: 20,
        openWindow: TimeSpan.FromMinutes(1));

    public RateLimitPartition<string> OpenPartition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subjectId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(subjectId))
        {
            return RateLimitPartition.GetNoLimiter("missing-subject");
        }

        var partitionKey = $"subject:{subjectId}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = OpenPermitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = OpenWindow,
            });
    }
}
