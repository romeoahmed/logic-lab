using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using TUnit.FsCheck;

namespace LogicLab.Domain.Tests;

public sealed class ProjectValueTests
{
    [Test]
    public async Task OrthogonalWireRoute_InputMutation_DoesNotChangeOwnedPoints()
    {
        var points = new[]
        {
            new GridPoint(0, 0),
            new GridPoint(4, 0),
        };
        var route = new OrthogonalWireRoute(points);

        points[1] = new GridPoint(9, 9);

        await Assert.That(route.Points[1]).IsEqualTo(new GridPoint(4, 0));
    }

    [Test, FsCheckProperty]
    public Property OrthogonalWireRoute_AnyPointSequences_EqualityMatchesSequenceEquality(
        GridPoint[] leftPoints,
        GridPoint[] rightPoints)
    {
        var first = new OrthogonalWireRoute(leftPoints);
        var equalCopy = new OrthogonalWireRoute(leftPoints);
        var second = new OrthogonalWireRoute(rightPoints);
        var expectedEqual = leftPoints.AsSpan().SequenceEqual(rightPoints);
        var set = new HashSet<OrthogonalWireRoute> { first };

        var equalityMatches = first.Equals(first)
            && first == equalCopy
            && equalCopy == first
            && first.Equals(equalCopy)
            && (first == second) == expectedEqual
            && first.Equals(second) == expectedEqual
            && (second == first) == expectedEqual;
        var hashContractHolds = first.GetHashCode() == equalCopy.GetHashCode()
            && (!expectedEqual || first.GetHashCode() == second.GetHashCode());
        var setMembershipMatches = set.Contains(equalCopy)
            && set.Contains(second) == expectedEqual;

        return (equalityMatches && hashContractHolds && setMembershipMatches)
            .Label("route equality, hashing, and set membership match point-sequence equality")
            .Collect($"left={leftPoints.Length}, right={rightPoints.Length}");
    }

    [Test, FsCheckProperty]
    public Property LogicVectorParameterValue_AnyValueSequences_EqualityMatchesSequenceEquality(
        LogicValue[] leftValues,
        LogicValue[] rightValues)
    {
        var first = new LogicVectorParameterValue(leftValues);
        var equalCopy = new LogicVectorParameterValue(leftValues);
        var second = new LogicVectorParameterValue(rightValues);
        var expectedEqual = leftValues.AsSpan().SequenceEqual(rightValues);
        var set = new HashSet<LogicVectorParameterValue> { first };

        var equalityMatches = first.Equals(first)
            && first == equalCopy
            && equalCopy == first
            && first.Equals(equalCopy)
            && (first == second) == expectedEqual
            && first.Equals(second) == expectedEqual
            && (second == first) == expectedEqual;
        var hashContractHolds = first.GetHashCode() == equalCopy.GetHashCode()
            && (!expectedEqual || first.GetHashCode() == second.GetHashCode());
        var setMembershipMatches = set.Contains(equalCopy)
            && set.Contains(second) == expectedEqual;

        return (equalityMatches && hashContractHolds && setMembershipMatches)
            .Label("vector parameter equality, hashing, and set membership match value-sequence equality")
            .Collect($"left={leftValues.Length}, right={rightValues.Length}");
    }
}
