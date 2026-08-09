using FsCheck;
using FsCheck.Fluent;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed record ClockEventCalendarCase(uint[] LogicalTimes)
{
    public override string ToString() => $"Calendar(events={LogicalTimes.Length})";
}

internal static class ClockEventCalendarArbitraries
{
    private const int MaximumEventCount = 32;
    private const int MaximumLogicalTime = 16;

    public static Arbitrary<ClockEventCalendarCase> ClockEventCalendar()
    {
        var generator =
            from eventCount in Gen.Choose(0, MaximumEventCount)
            from logicalTimes in Gen.Choose(0, MaximumLogicalTime).ArrayOf(eventCount)
            select new ClockEventCalendarCase(
                [.. logicalTimes.Select(static logicalTime => checked((uint)logicalTime))]);

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<ClockEventCalendarCase> Shrink(
        ClockEventCalendarCase sample)
    {
        for (var index = 0; index < sample.LogicalTimes.Length; index++)
        {
            yield return new ClockEventCalendarCase(
                [.. sample.LogicalTimes.Where((_, candidateIndex) => candidateIndex != index)]);

            var logicalTime = sample.LogicalTimes[index];
            if (logicalTime == 0)
            {
                continue;
            }

            var logicalTimes = (uint[])sample.LogicalTimes.Clone();
            logicalTimes[index] = logicalTime / 2;
            yield return new ClockEventCalendarCase(logicalTimes);
        }
    }
}

internal sealed class ClockEventCalendarTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(ClockEventCalendarArbitraries) })]
    public Property Schedule_GeneratedValidBuckets_ReturnsStableSortedSnapshots(
        ClockEventCalendarCase sample)
    {
        var calendar = new ClockEventCalendar();
        var events = sample.LogicalTimes
            .Select((logicalTime, evaluatorOrdinal) => Event(
                evaluatorOrdinal,
                logicalTime))
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
