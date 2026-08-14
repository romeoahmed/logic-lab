using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class ExactStateIndexTests
{
    [Test]
    [Arguments("retained", "retained", true)]
    [Arguments("retained", "distinct", false)]
    public async Task Contains_ForcedHashCollision_UsesExactStateEquality(
        string retainedState,
        string candidateState,
        bool expected)
    {
        var index = new ExactStateIndex<string, int>(
            (_, _) => 0,
            (left, right, _) => string.Equals(left, right, StringComparison.Ordinal));
        index.Add(0, retainedState);

        var containsCandidate = index.Contains(
            candidateState,
            out var fingerprint,
            CancellationToken.None);
        if (!containsCandidate)
        {
            index.Add(fingerprint, candidateState);
        }

        var containsRepeat = index.Contains(
            candidateState,
            out _,
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(containsCandidate).IsEqualTo(expected);
            await Assert.That(containsRepeat).IsTrue();
        }
    }
}
