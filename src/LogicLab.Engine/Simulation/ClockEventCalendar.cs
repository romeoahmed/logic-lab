namespace LogicLab.Engine.Simulation;

internal sealed record ScheduledClockTransition(
    int EvaluatorOrdinal,
    int DriverOrdinal);

internal readonly record struct ScheduledClockEvent(
    ScheduledClockTransition Transition,
    ulong LogicalTime);

internal sealed class ClockEventCalendar
{
    private readonly PriorityQueue<ulong, ulong> logicalTimes = new();
    private readonly Dictionary<
        ulong,
        SortedDictionary<int, ScheduledClockTransition>> transitionsByTime = [];

    public ulong? PeekLogicalTime()
    {
        return logicalTimes.TryPeek(out var logicalTime, out _)
            ? logicalTime
            : null;
    }

    public ScheduledClockTransition[] ReadTimeBucket(ulong logicalTime)
    {
        return transitionsByTime.TryGetValue(logicalTime, out var transitions)
            ? [.. transitions.Values]
            : [];
    }

    public void Schedule(ScheduledClockEvent scheduledEvent)
    {
        if (!transitionsByTime.TryGetValue(
                scheduledEvent.LogicalTime,
                out var transitions))
        {
            transitions = [];
            transitionsByTime.Add(scheduledEvent.LogicalTime, transitions);
            logicalTimes.Enqueue(
                scheduledEvent.LogicalTime,
                scheduledEvent.LogicalTime);
        }

        transitions.Add(
            scheduledEvent.Transition.EvaluatorOrdinal,
            scheduledEvent.Transition);
    }

    public void CommitTimeBucket(
        ulong logicalTime,
        IReadOnlyList<ScheduledClockEvent> nextEvents)
    {
        if (!logicalTimes.TryPeek(out var earliestTime, out _)
            || earliestTime != logicalTime
            || !transitionsByTime.ContainsKey(logicalTime))
        {
            throw new InvalidOperationException(
                "The committed clock-event bucket is not the earliest bucket.");
        }

        _ = logicalTimes.Dequeue();
        _ = transitionsByTime.Remove(logicalTime);
        foreach (var scheduledEvent in nextEvents)
        {
            Schedule(scheduledEvent);
        }
    }
}
