using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class ClockEventCalendarTests
{
    [Test]
    public async Task ReadTimeBucket_SimultaneousEvents_ReturnsStableNonConsumingSnapshot()
    {
        var calendar = new ClockEventCalendar();
        calendar.Schedule(Event(evaluatorOrdinal: 2, logicalTime: 5));
        calendar.Schedule(Event(evaluatorOrdinal: 0, logicalTime: 5));
        calendar.Schedule(Event(evaluatorOrdinal: 1, logicalTime: 5));

        var first = calendar.ReadTimeBucket(5);
        var second = calendar.ReadTimeBucket(5);

        using (Assert.Multiple())
        {
            await Assert.That(first.Select(item => item.EvaluatorOrdinal))
                .IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
            await Assert.That(second.Select(item => item.EvaluatorOrdinal))
                .IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
            await Assert.That(calendar.PeekLogicalTime()).IsEqualTo(5UL);
        }
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
}
