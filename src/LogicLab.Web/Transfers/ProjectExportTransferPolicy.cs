using System.Threading.RateLimiting;
using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Transfers;

internal sealed record ProjectExportTransferPolicy
{
    public const string DownloadRateLimitPolicyName = "project-export-download";

    public ProjectExportTransferPolicy(
        int maximumConcurrentDownloads,
        int downloadPermitLimit,
        TimeSpan downloadWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumConcurrentDownloads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(downloadPermitLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            downloadWindow,
            TimeSpan.Zero);

        MaximumConcurrentDownloads = maximumConcurrentDownloads;
        DownloadPermitLimit = downloadPermitLimit;
        DownloadWindow = downloadWindow;
    }

    public int MaximumConcurrentDownloads { get; }

    public int DownloadPermitLimit { get; }

    public TimeSpan DownloadWindow { get; }

    public static ProjectExportTransferPolicy Default { get; } = new(
        maximumConcurrentDownloads: 8,
        downloadPermitLimit: 20,
        downloadWindow: TimeSpan.FromMinutes(1));

    public RateLimitPartition<string> ConcurrentTransferPartition(
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetEndpoint()?.Metadata
                .GetMetadata<ProjectExportTransferMetadata>() is null
            ? RateLimitPartition.GetNoLimiter("non-export-transfer")
            : RateLimitPartition.GetConcurrencyLimiter(
                "project-export-transfers",
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = MaximumConcurrentDownloads,
                    QueueLimit = 0,
                });
    }

    public RateLimitPartition<string> DownloadPartition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var caller = WorkspaceCallerAdapter.FromTransferPrincipal(context.User);
        var partitionKey = caller switch
        {
            AuthenticatedWorkspaceCaller authenticated =>
                $"subject:{authenticated.SubjectId.Value}",
            AnonymousBrowserWorkspaceCaller anonymous =>
                $"browser:{anonymous.BrowserId.Value}",
            _ => "invalid-caller",
        };
        return IngressRateLimiting.FixedWindowPartition(
            partitionKey,
            DownloadPermitLimit,
            DownloadWindow);
    }
}

internal sealed class ProjectExportTransferMetadata
{
    private ProjectExportTransferMetadata()
    {
    }

    public static ProjectExportTransferMetadata Instance { get; } = new();
}
