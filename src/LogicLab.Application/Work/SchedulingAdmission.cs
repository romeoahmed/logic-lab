using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Work;

internal sealed class SchedulingAdmission
{
    private readonly SchedulingPolicy policy;
    private readonly TimeProvider timeProvider;
    private readonly ulong globalRequestLimit;
    private readonly ulong subjectRequestLimit;
    private readonly int partitionLimit;
    private readonly TimeSpan window;
    private readonly Dictionary<WorkspaceCaller, ulong> partitions = [];
    private readonly Queue<PartitionExpiry> expirations = [];
    private long globalWindowStartTimestamp;
    private ulong globalRequestCount;
    private bool hasGlobalWindow;

    public SchedulingAdmission(
        SchedulingPolicy policy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.policy = policy;
        this.timeProvider = timeProvider;
        globalRequestLimit = policy.GetMaximum(
            SchedulingDimension.AdmissionRequestsGlobal);
        subjectRequestLimit = policy.GetMaximum(
            SchedulingDimension.AdmissionRequestsPerSubject);
        partitionLimit = policy.GetInt32Maximum(
            SchedulingDimension.AdmissionPartitionCount);
        window = TimeSpan.FromMilliseconds(checked((long)policy.GetMaximum(
            SchedulingDimension.AdmissionWindowMilliseconds)));
    }

    public bool TryAdmitUnderLock(
        WorkspaceCaller caller,
        [NotNullWhen(false)] out PolicyEvidenceProjection? rejectionEvidence)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var nowTimestamp = timeProvider.GetTimestamp();
        ResetGlobalWindowIfExpired(nowTimestamp);
        if (globalRequestCount >= globalRequestLimit)
        {
            rejectionEvidence = policy.Evidence(
                SchedulingDimension.AdmissionRequestsGlobal,
                ObservedAttempt(globalRequestCount));
            return false;
        }

        globalRequestCount++;
        PruneExpired(nowTimestamp);

        var hasPartition = partitions.TryGetValue(caller, out var requestCount);
        if (hasPartition && requestCount >= subjectRequestLimit)
        {
            rejectionEvidence = policy.Evidence(
                SchedulingDimension.AdmissionRequestsPerSubject,
                ObservedAttempt(requestCount));
            return false;
        }

        if (!hasPartition && partitions.Count >= partitionLimit)
        {
            rejectionEvidence = policy.Evidence(
                SchedulingDimension.AdmissionPartitionCount,
                checked((ulong)partitions.Count + 1));
            return false;
        }

        if (hasPartition)
        {
            partitions[caller] = requestCount + 1;
        }
        else
        {
            partitions.Add(caller, 1);
            expirations.Enqueue(new PartitionExpiry(
                caller,
                nowTimestamp));
        }

        rejectionEvidence = null;
        return true;
    }

    public void ClearUnderLock()
    {
        partitions.Clear();
        expirations.Clear();
        globalRequestCount = 0;
        hasGlobalWindow = false;
    }

    private void PruneExpired(long nowTimestamp)
    {
        while (expirations.TryPeek(out var expiry)
            && HasWindowElapsed(expiry.StartTimestamp, nowTimestamp))
        {
            _ = expirations.Dequeue();
            _ = partitions.Remove(expiry.Caller);
        }
    }

    private void ResetGlobalWindowIfExpired(long nowTimestamp)
    {
        if (hasGlobalWindow
            && !HasWindowElapsed(globalWindowStartTimestamp, nowTimestamp))
        {
            return;
        }

        globalWindowStartTimestamp = nowTimestamp;
        globalRequestCount = 0;
        hasGlobalWindow = true;
    }

    private bool HasWindowElapsed(long startTimestamp, long nowTimestamp)
    {
        return timeProvider.GetElapsedTime(startTimestamp, nowTimestamp) >= window;
    }

    private static ulong ObservedAttempt(ulong admitted)
    {
        return admitted == ulong.MaxValue ? ulong.MaxValue : admitted + 1;
    }

    private sealed record PartitionExpiry(
        WorkspaceCaller Caller,
        long StartTimestamp);
}
