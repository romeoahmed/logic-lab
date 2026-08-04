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
        return SequenceEqualityMatchesValueEquality(
            leftPoints,
            rightPoints,
            static points => new OrthogonalWireRoute(points),
            "route");
    }

    [Test, FsCheckProperty]
    public Property LogicVectorParameterValue_AnyValueSequences_EqualityMatchesSequenceEquality(
        LogicValue[] leftValues,
        LogicValue[] rightValues)
    {
        return SequenceEqualityMatchesValueEquality(
            leftValues,
            rightValues,
            static values => new LogicVectorParameterValue(values),
            "vector parameter");
    }

    [Test]
    public async Task CollectionParameterValues_InputMutation_DoesNotChangeOwnedValues()
    {
        var slices = new[] { new BitSlice(0, 1), new BitSlice(1, 2) };
        var widths = new uint[] { 1, 2 };
        var sliceValue = new SlicesParameterValue(slices);
        var widthValue = new WidthsParameterValue(widths);

        slices[0] = new BitSlice(99, 99);
        widths[0] = 99;

        using (Assert.Multiple())
        {
            await Assert.That(sliceValue.Values)
                .IsEquivalentTo(
                    [new BitSlice(0, 1), new BitSlice(1, 2)],
                    TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(widthValue.Values)
                .IsEquivalentTo(
                    [1U, 2U],
                    TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    [Test, FsCheckProperty]
    public Property SlicesParameterValue_AnySliceSequences_EqualityMatchesSequenceEquality(
        uint[] leftOffsets,
        uint[] leftLengths,
        uint[] rightOffsets,
        uint[] rightLengths)
    {
        var left = ToSlices(leftOffsets, leftLengths);
        var right = ToSlices(rightOffsets, rightLengths);

        return SequenceEqualityMatchesValueEquality(
            left,
            right,
            static values => new SlicesParameterValue(values),
            "slices parameter");
    }

    [Test, FsCheckProperty]
    public Property WidthsParameterValue_AnyWidthSequences_EqualityMatchesSequenceEquality(
        uint[] leftWidths,
        uint[] rightWidths)
    {
        return SequenceEqualityMatchesValueEquality(
            leftWidths,
            rightWidths,
            static values => new WidthsParameterValue(values),
            "widths parameter");
    }

    private static Property SequenceEqualityMatchesValueEquality<TItem, TValue>(
        TItem[] left,
        TItem[] right,
        Func<IReadOnlyList<TItem>, TValue> create,
        string subject)
        where TValue : notnull
    {
        var first = create(left);
        var equalCopy = create(left);
        var second = create(right);
        var expectedEqual = left.AsSpan().SequenceEqual(right);
        var equality = EqualityComparer<TValue>.Default;
        var set = new HashSet<TValue> { first };

        var matches = equality.Equals(first, equalCopy)
            && equality.Equals(first, second) == expectedEqual
            && first.GetHashCode() == equalCopy.GetHashCode()
            && (!expectedEqual || first.GetHashCode() == second.GetHashCode())
            && set.Contains(equalCopy)
            && set.Contains(second) == expectedEqual;

        return matches
            .Label($"{subject} equality and hashing match sequence equality")
            .Collect($"left={left.Length}, right={right.Length}");
    }

    private static BitSlice[] ToSlices(uint[] offsets, uint[] lengths)
    {
        return [.. offsets.Zip(lengths, static (offset, length) => new BitSlice(offset, length))];
    }
}
