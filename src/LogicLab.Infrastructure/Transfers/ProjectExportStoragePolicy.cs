namespace LogicLab.Infrastructure.Transfers;

public sealed record ProjectExportStoragePolicy
{
    public ProjectExportStoragePolicy(
        int maximumPublishedExports,
        ulong maximumPublishedCarrierBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPublishedExports);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPublishedCarrierBytes);

        MaximumPublishedExports = maximumPublishedExports;
        MaximumPublishedCarrierBytes = maximumPublishedCarrierBytes;
    }

    public int MaximumPublishedExports { get; }

    public ulong MaximumPublishedCarrierBytes { get; }

    public static ProjectExportStoragePolicy Default { get; } = new(
        maximumPublishedExports: 128,
        maximumPublishedCarrierBytes: 512UL * 1024 * 1024);
}
