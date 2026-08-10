using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using TUnit.FsCheck;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectValueTests
{
    [Test, FsCheckProperty]
    public Property OrthogonalWireRoute_NonemptyInputMutation_DoesNotChangeOwnedPoints(
        NonEmptyArray<GridPoint> generatedPoints)
    {
        var points = generatedPoints.Get.ToArray();
        var route = new OrthogonalWireRoute(points);
        var expected = route.Points.ToArray();

        points[0] = new GridPoint(points[0].X ^ 1, points[0].Y ^ 1);

        return route.Points.SequenceEqual(expected)
            .Label("route owns its point sequence")
            .Collect($"points={points.Length}");
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

    [Test, FsCheckProperty]
    public Property SlicesParameterValue_NonemptyInputMutation_DoesNotChangeOwnedValues(
        NonEmptyArray<uint> generatedOffsets,
        NonEmptyArray<uint> generatedLengths)
    {
        var slices = ToSlices(generatedOffsets.Get, generatedLengths.Get);
        var sliceValue = new SlicesParameterValue(slices);
        var expected = sliceValue.Values.ToArray();

        slices[0] = new BitSlice(slices[0].Offset ^ 1U, slices[0].Length ^ 1U);

        return sliceValue.Values.SequenceEqual(expected)
            .Label("slices parameter owns its value sequence")
            .Collect($"slices={slices.Length}");
    }

    [Test, FsCheckProperty]
    public Property WidthsParameterValue_NonemptyInputMutation_DoesNotChangeOwnedValues(
        NonEmptyArray<uint> generatedWidths)
    {
        var widths = generatedWidths.Get.ToArray();
        var widthValue = new WidthsParameterValue(widths);
        var expected = widthValue.Values.ToArray();

        widths[0] ^= 1U;

        return widthValue.Values.SequenceEqual(expected)
            .Label("widths parameter owns its value sequence")
            .Collect($"widths={widths.Length}");
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
