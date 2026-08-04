namespace LogicLab.Engine.Simulation;

internal readonly record struct ScheduledClockTransition(
    int EvaluatorOrdinal,
    int DriverOrdinal);

internal readonly record struct ScheduledClockEvent(
    ScheduledClockTransition Transition,
    ulong LogicalTime);

internal sealed class ClockEventCalendar
{
    private readonly SortedDictionary<
        ulong,
        SortedDictionary<int, ScheduledClockTransition>> buckets = [];

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
}
