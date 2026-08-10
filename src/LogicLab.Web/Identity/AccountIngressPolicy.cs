using System.Threading.RateLimiting;

namespace LogicLab.Web.Identity;

internal sealed record AccountIngressPolicy(
    int LoginPermitLimit,
    TimeSpan LoginWindow,
    int RegistrationPermitLimit,
    TimeSpan RegistrationWindow)
{
    public const string LoginRateLimitPolicyName = "account-login";
    public const string RegistrationRateLimitPolicyName = "account-registration";
    public const int MaximumRequestBodyBytes = 4096;

    public static AccountIngressPolicy Default { get; } = new(
        LoginPermitLimit: 10,
        LoginWindow: TimeSpan.FromMinutes(1),
        RegistrationPermitLimit: 5,
        RegistrationWindow: TimeSpan.FromMinutes(1));

    public RateLimitPartition<string> LoginPartition(HttpContext context)
        => Partition(context, LoginPermitLimit, LoginWindow);

    public RateLimitPartition<string> RegistrationPartition(HttpContext context)
        => Partition(context, RegistrationPermitLimit, RegistrationWindow);

    private static RateLimitPartition<string> Partition(
        HttpContext context,
        int permitLimit,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("read");
        }

        var partitionKey = context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = window,
            });
    }
}

internal static class AccountInputLimits
{
    public const int MaximumEmailLength = 256;
    public const int MaximumPasswordLength = 100;
}
