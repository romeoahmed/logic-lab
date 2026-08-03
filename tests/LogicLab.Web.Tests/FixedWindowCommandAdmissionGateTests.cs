using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

public sealed class FixedWindowCommandAdmissionGateTests
{
    [Test]
    [Arguments(999, false)]
    [Arguments(1_000, true)]
    [Arguments(1_001, true)]
    public async Task FixedWindowCommandAdmissionGate_ElapsedTime_ResetsOnlyAtOrAfterBoundary(
        int elapsedMilliseconds,
        bool expectedAdmission)
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
        var gate = new FixedWindowCommandAdmissionGate(
            maximumAdmissions: 2,
            window: TimeSpan.FromSeconds(1),
            timeProvider);

        var first = gate.TryAdmit();
        var second = gate.TryAdmit();
        var rejected = gate.TryAdmit();
        timeProvider.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        var afterElapsedTime = gate.TryAdmit();

        using (Assert.Multiple())
        {
            await Assert.That(first).IsTrue();
            await Assert.That(second).IsTrue();
            await Assert.That(rejected).IsFalse();
            await Assert.That(afterElapsedTime).IsEqualTo(expectedAdmission);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long timestamp;

        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override long GetTimestamp() => timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan elapsed)
        {
            UtcNow += elapsed;
            timestamp += elapsed.Ticks;
        }
    }
}
