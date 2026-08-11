using System.Security.Claims;
using System.Threading.RateLimiting;

namespace LogicLab.Web.Identity;

internal sealed record AccountIngressPolicy(
    int LoginPermitLimit,
    TimeSpan LoginWindow,
    int RegistrationPermitLimit,
    TimeSpan RegistrationWindow,
    int LogoutPermitLimit,
    TimeSpan LogoutWindow)
{
    public const string LoginRateLimitPolicyName = "account-login";
    public const string RegistrationRateLimitPolicyName = "account-registration";
    public const string LogoutRateLimitPolicyName = "account-logout";
    public const int MaximumRequestBodyBytes = 4096;

    public static AccountIngressPolicy Default { get; } = new(
        LoginPermitLimit: 10,
        LoginWindow: TimeSpan.FromMinutes(1),
        RegistrationPermitLimit: 5,
        RegistrationWindow: TimeSpan.FromMinutes(1),
        LogoutPermitLimit: 5,
        LogoutWindow: TimeSpan.FromMinutes(1));

    public RateLimitPartition<string> LoginPartition(HttpContext context)
        => Partition(context, LoginPermitLimit, LoginWindow);

    public RateLimitPartition<string> RegistrationPartition(HttpContext context)
        => Partition(context, RegistrationPermitLimit, RegistrationWindow);

    public RateLimitPartition<string> LogoutPartition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subjectId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = string.IsNullOrEmpty(subjectId)
            ? ClientPartitionKey(context)
            : $"subject:{subjectId}";
        return Partition(context, LogoutPermitLimit, LogoutWindow, partitionKey);
    }

    private static RateLimitPartition<string> Partition(
        HttpContext context,
        int permitLimit,
        TimeSpan window,
        string? partitionKey = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("read");
        }

        partitionKey ??= ClientPartitionKey(context);
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

    private static string ClientPartitionKey(HttpContext context)
    {
        return $"client:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}

internal static class AccountInputLimits
{
    public const int MaximumEmailLength = 256;
    public const int MaximumPasswordLength = 100;
}
