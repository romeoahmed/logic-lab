using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

public sealed class FixedWindowCommandAdmissionGateTests
{
    [Test]
    public async Task FixedWindowCommandAdmissionGate_WindowCapacityExceeded_RejectsUntilNextWindow()
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
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var nextWindow = gate.TryAdmit();

        using (Assert.Multiple())
        {
            await Assert.That(first).IsTrue();
            await Assert.That(second).IsTrue();
            await Assert.That(rejected).IsFalse();
            await Assert.That(nextWindow).IsTrue();
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
