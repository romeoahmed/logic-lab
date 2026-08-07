using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal readonly record struct ScheduledClockTransition(
    int EvaluatorOrdinal,
    int DriverOrdinal);

internal readonly record struct ScheduledClockEvent(
    ScheduledClockTransition Transition,
    ulong LogicalTime);

internal sealed class ClockEventCalendar
{
    private const ulong SlotBytes = sizeof(ulong);
    private readonly SortedDictionary<
        ulong,
        SortedDictionary<int, ScheduledClockTransition>> buckets = [];

    internal ulong RetainedOwnedBufferBytes
    {
        get
        {
            var transitionCount = buckets.Aggregate(
                0UL,
                (count, bucket) => checked(count + (ulong)bucket.Value.Count));
            return OwnedBufferBytes((ulong)buckets.Count, transitionCount);
        }
    }

    internal static ulong CandidateOwnedBufferBytes(
        SimulationIr ir,
        ulong logicalTimeOrigin)
    {
        var logicalTimes = new HashSet<ulong>();
        ulong transitionCount = 0;
        foreach (var evaluator in ir.Evaluators)
        {
            if (evaluator.Kind != SimulationEvaluatorKind.ClockSource)
            {
                continue;
            }

            var firstTransition = evaluator.ClockSchedule!.FirstTransition;
            if (firstTransition > ulong.MaxValue - logicalTimeOrigin)
            {
                continue;
            }

            logicalTimes.Add(checked(logicalTimeOrigin + firstTransition));
            transitionCount++;
        }

        return OwnedBufferBytes((ulong)logicalTimes.Count, transitionCount);
    }

    public ulong? PeekLogicalTime()
    {
        return buckets.Count == 0 ? null : buckets.First().Key;
    }

    public ScheduledClockTransition[] ReadTimeBucket(ulong logicalTime)
    {
        return buckets.TryGetValue(logicalTime, out var transitions)
            ? [.. transitions.Values]
            : [];
    }

    public void Schedule(ScheduledClockEvent scheduledEvent)
    {
        if (!buckets.TryGetValue(
                scheduledEvent.LogicalTime,
                out var transitions))
        {
            transitions = [];
            buckets.Add(scheduledEvent.LogicalTime, transitions);
        }

        transitions.Add(
            scheduledEvent.Transition.EvaluatorOrdinal,
            scheduledEvent.Transition);
    }

    public void CommitTimeBucket(
        ulong logicalTime,
        IReadOnlyList<ScheduledClockEvent> nextEvents)
    {
        if (buckets.Count == 0 || buckets.First().Key != logicalTime)
        {
            throw new InvalidOperationException(
                "The committed clock-event bucket is not the earliest bucket.");
        }

        _ = buckets.Remove(logicalTime);
        foreach (var scheduledEvent in nextEvents)
        {
            Schedule(scheduledEvent);
        }
    }

    private static ulong OwnedBufferBytes(
        ulong bucketCount,
        ulong transitionCount)
    {
        return checked(
            (bucketCount * 2UL * SlotBytes)
            + (transitionCount * 2UL * SlotBytes));
    }
}
