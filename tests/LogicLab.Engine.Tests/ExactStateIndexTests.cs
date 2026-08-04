using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

public sealed class ExactStateIndexTests
{
    [Test]
    public async Task Contains_DistinctFingerprints_SkipsExactComparisons()
    {
        var exactComparisonCount = 0;
        var index = new ExactStateIndex<string, int>(
            (value, _) => int.Parse(
                value,
                System.Globalization.CultureInfo.InvariantCulture),
            (left, right, _) =>
            {
                exactComparisonCount++;
                return string.Equals(left, right, StringComparison.Ordinal);
            });

        for (var value = 0; value < 100; value++)
        {
            var candidate = value.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            var contains = index.Contains(
                candidate,
                out var fingerprint,
                CancellationToken.None);
            index.Add(fingerprint, candidate);

            await Assert.That(contains).IsFalse();
        }

        await Assert.That(exactComparisonCount).IsEqualTo(0);
    }

    [Test]
    public async Task Contains_CollidingDistinctStates_RequiresExactEquality()
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

    [Test]
    public async Task Contains_CancellationDuringCollisionScan_StopsBeforeNextComparison()
    {
        using var cancellation = new CancellationTokenSource();
        var exactComparisonCount = 0;
        var index = new ExactStateIndex<string, int>(
            (_, _) => 0,
            (left, right, _) =>
            {
                exactComparisonCount++;
                cancellation.Cancel();
                return string.Equals(left, right, StringComparison.Ordinal);
            });
        index.Add(0, "first");
        index.Add(0, "second");

        await Assert.That(() => index.Contains(
                "candidate",
                out _,
                cancellation.Token))
            .ThrowsExactly<OperationCanceledException>();
        await Assert.That(exactComparisonCount).IsEqualTo(1);
    }

    [Test]
    public async Task Contains_CancellationDuringFingerprinting_StopsBeforeLookup()
    {
        using var cancellation = new CancellationTokenSource();
        var index = new ExactStateIndex<string, int>(
            (_, _) =>
            {
                cancellation.Cancel();
                return 0;
            },
            (left, right, _) => string.Equals(left, right, StringComparison.Ordinal));

        await Assert.That(() => index.Contains(
                "candidate",
                out _,
                cancellation.Token))
            .ThrowsExactly<OperationCanceledException>();
    }
}
