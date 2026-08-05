using FsCheck;
using FsCheck.Fluent;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class ClockEventCalendarTests
{
    private const uint LogicalTimeBucketCount = 17;

    [Test, FsCheckProperty]
    public Property Schedule_ArbitraryBuckets_ReturnsStableSortedSnapshots(
        int[] logicalTimes)
    {
        var calendar = new ClockEventCalendar();
        var events = logicalTimes
            .Select((logicalTime, evaluatorOrdinal) => Event(
                evaluatorOrdinal,
                unchecked((uint)logicalTime) % LogicalTimeBucketCount))
            .ToArray();
        foreach (var scheduledEvent in events.Reverse())
        {
            calendar.Schedule(scheduledEvent);
        }

        var expectedBuckets = events
            .GroupBy(static scheduledEvent => scheduledEvent.LogicalTime)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static scheduledEvent =>
                        scheduledEvent.Transition.EvaluatorOrdinal)
                    .Order()
                    .ToArray());
        var expectedTime = expectedBuckets.Count == 0
            ? (ulong?)null
            : expectedBuckets.Keys.Min();
        var matches = calendar.PeekLogicalTime() == expectedTime
            && expectedBuckets.All(bucket =>
                calendar.ReadTimeBucket(bucket.Key)
                    .Select(static transition => transition.EvaluatorOrdinal)
                    .SequenceEqual(bucket.Value)
                && calendar.ReadTimeBucket(bucket.Key)
                    .Select(static transition => transition.EvaluatorOrdinal)
                    .SequenceEqual(bucket.Value))
            && calendar.PeekLogicalTime() == expectedTime;

        return matches
            .Label("calendar matches the stable sorted bucket model")
            .Collect(CountBucket(events.Length))
            .Collect($"buckets={expectedBuckets.Count}");
    }

    [Test]
    public async Task CommitTimeBucket_EarliestBucket_ReplacesOnlyCommittedEvents()
    {
        var calendar = new ClockEventCalendar();
        calendar.Schedule(Event(evaluatorOrdinal: 0, logicalTime: 5));
        calendar.Schedule(Event(evaluatorOrdinal: 1, logicalTime: 7));

        calendar.CommitTimeBucket(
            5,
            [Event(evaluatorOrdinal: 0, logicalTime: 9)]);

        using (Assert.Multiple())
        {
            await Assert.That(calendar.PeekLogicalTime()).IsEqualTo(7UL);
            await Assert.That(calendar.ReadTimeBucket(5)).IsEmpty();
            await Assert.That(calendar.ReadTimeBucket(7).Single().EvaluatorOrdinal)
                .IsEqualTo(1);
            await Assert.That(calendar.ReadTimeBucket(9).Single().EvaluatorOrdinal)
                .IsEqualTo(0);
        }
    }

    private static ScheduledClockEvent Event(
        int evaluatorOrdinal,
        ulong logicalTime)
    {
        return new ScheduledClockEvent(
            new ScheduledClockTransition(
                evaluatorOrdinal,
                DriverOrdinal: evaluatorOrdinal),
            logicalTime);
    }

    private static string CountBucket(int count)
    {
        return count switch
        {
            0 => "events=0",
            1 => "events=1",
            <= 8 => "events=2..8",
            <= 32 => "events=9..32",
            _ => "events=33+",
        };
    }
}
