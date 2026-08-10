using FsCheck;
using FsCheck.Fluent;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class ExactStateIndexTests
{
    [Test, FsCheckProperty]
    public Property Contains_AnyHashCollision_UsesExactStateEquality(
        NonNull<string> retainedState,
        NonNull<string> candidateState)
    {
        var index = new ExactStateIndex<string, int>(
            (_, _) => 0,
            (left, right, _) => string.Equals(left, right, StringComparison.Ordinal));
        index.Add(0, retainedState.Get);

        var containsCandidate = index.Contains(
            candidateState.Get,
            out var fingerprint,
            CancellationToken.None);
        var expected = string.Equals(
            retainedState.Get,
            candidateState.Get,
            StringComparison.Ordinal);
        if (!containsCandidate)
        {
            index.Add(fingerprint, candidateState.Get);
        }

        var containsRepeat = index.Contains(
            candidateState.Get,
            out _,
            CancellationToken.None);

        return (containsCandidate == expected && containsRepeat)
            .Label("forced fingerprint collisions use exact equality")
            .Collect(expected ? "equal states" : "distinct collision");
    }
}
