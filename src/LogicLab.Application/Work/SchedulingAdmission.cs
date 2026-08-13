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
    private readonly Dictionary<WorkspaceCaller, PartitionState> partitions = [];
    private readonly Queue<PartitionExpiry> expirations = [];
    private long globalWindowStartTimestamp;
    private ulong globalRequestCount;
    private ulong nextGeneration;
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
        partitionLimit = MaximumAsInt(
            policy,
            SchedulingDimension.AdmissionPartitionCount);
        window = TimeSpan.FromMilliseconds(checked((long)policy.GetMaximum(
            SchedulingDimension.AdmissionWindowMilliseconds)));
    }

    public bool TryAdmitUnderLock(
        WorkspaceCaller caller,
        out PolicyEvidenceProjection? rejectionEvidence)
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

        var hasPartition = partitions.TryGetValue(caller, out var partition);
        if (hasPartition && partition!.RequestCount >= subjectRequestLimit)
        {
            rejectionEvidence = policy.Evidence(
                SchedulingDimension.AdmissionRequestsPerSubject,
                ObservedAttempt(partition.RequestCount));
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
            partition!.RequestCount++;
        }
        else
        {
            var generation = NextGeneration();
            partitions.Add(
                caller,
                new PartitionState(generation));
            expirations.Enqueue(new PartitionExpiry(
                caller,
                nowTimestamp,
                generation));
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
            if (partitions.TryGetValue(expiry.Caller, out var partition)
                && partition.Generation == expiry.Generation)
            {
                _ = partitions.Remove(expiry.Caller);
            }
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

    private ulong NextGeneration()
    {
        nextGeneration = nextGeneration == ulong.MaxValue
            ? 1
            : nextGeneration + 1;
        return nextGeneration;
    }

    private static ulong ObservedAttempt(ulong admitted)
    {
        return admitted == ulong.MaxValue ? ulong.MaxValue : admitted + 1;
    }

    private static int MaximumAsInt(
        SchedulingPolicy policy,
        SchedulingDimension dimension)
    {
        var maximum = policy.GetMaximum(dimension);
        if (maximum > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                $"The {SchedulingPolicy.DimensionToken(dimension)} limit must fit Int32.");
        }

        return checked((int)maximum);
    }

    private sealed class PartitionState(ulong generation)
    {
        public ulong Generation { get; } = generation;

        public ulong RequestCount { get; set; } = 1;
    }

    private sealed record PartitionExpiry(
        WorkspaceCaller Caller,
        long StartTimestamp,
        ulong Generation);
}
