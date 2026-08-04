using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class ExactStateIndexTests
{
    [Test]
    public async Task Contains_HashCollision_RequiresExactStateEquality()
    {
        var index = new ExactStateIndex<string, int>(
            (_, _) => 0,
            (left, right, _) => string.Equals(left, right, StringComparison.Ordinal));
        index.Add(0, "left");

        var containsDistinct = index.Contains(
            "right",
            out var fingerprint,
            CancellationToken.None);
        index.Add(fingerprint, "right");
        var containsRepeat = index.Contains(
            "right",
            out _,
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(containsDistinct).IsFalse();
            await Assert.That(containsRepeat).IsTrue();
        }
    }
}
