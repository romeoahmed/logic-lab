using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

internal sealed class FixedWindowCommandAdmissionGateTests
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

}
