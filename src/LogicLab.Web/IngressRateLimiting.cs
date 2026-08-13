using System.Threading.RateLimiting;

namespace LogicLab.Web;

internal static class IngressRateLimiting
{
    public static RateLimitPartition<string> FixedWindowPartition(
        string partitionKey,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => FixedWindowOptions(
                permitLimit,
                window,
                autoReplenishment: false));

    public static RateLimiter FixedWindow(
        int permitLimit,
        TimeSpan window) =>
        new FixedWindowRateLimiter(FixedWindowOptions(
            permitLimit,
            window,
            autoReplenishment: true));

    private static FixedWindowRateLimiterOptions FixedWindowOptions(
        int permitLimit,
        TimeSpan window,
        bool autoReplenishment) =>
        new()
        {
            AutoReplenishment = autoReplenishment,
            PermitLimit = permitLimit,
            QueueLimit = 0,
            Window = window,
        };
}
