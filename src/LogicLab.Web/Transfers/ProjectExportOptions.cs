using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Transfers;

namespace LogicLab.Web.Transfers;

internal sealed class ProjectExportOptions
{
    public const string ConfigurationSectionName = "LogicLab:ProjectExports";

    public int MaximumConcurrentPreparations { get; set; } =
        ProjectExportPreparationPolicy.Default.MaximumConcurrentPreparations;

    public int MaximumConcurrentDownloads { get; set; } =
        ProjectExportTransferPolicy.Default.MaximumConcurrentDownloads;

    public int DownloadPermitLimit { get; set; } =
        ProjectExportTransferPolicy.Default.DownloadPermitLimit;

    public int DownloadWindowSeconds { get; set; } = checked(
        (int)ProjectExportTransferPolicy.Default.DownloadWindow.TotalSeconds);

    public int MaximumPublishedExports { get; set; } =
        ProjectExportStoragePolicy.Default.MaximumPublishedExports;

    public ulong MaximumPublishedCarrierBytes { get; set; } =
        ProjectExportStoragePolicy.Default.MaximumPublishedCarrierBytes;

    internal bool IsValid() =>
        MaximumConcurrentPreparations > 0
        && MaximumConcurrentDownloads > 0
        && DownloadPermitLimit > 0
        && DownloadWindowSeconds > 0
        && MaximumPublishedExports > 0
        && MaximumPublishedCarrierBytes > 0;

    internal ProjectExportPreparationPolicy CreatePreparationPolicy() =>
        new(MaximumConcurrentPreparations);

    internal ProjectExportTransferPolicy CreateTransferPolicy() =>
        new(
            MaximumConcurrentDownloads,
            DownloadPermitLimit,
            TimeSpan.FromSeconds(DownloadWindowSeconds));

    internal ProjectExportStoragePolicy CreateStoragePolicy() =>
        new(MaximumPublishedExports, MaximumPublishedCarrierBytes);
}
