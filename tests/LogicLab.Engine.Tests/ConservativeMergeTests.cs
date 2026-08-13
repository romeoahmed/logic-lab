using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class ConservativeMergeTests
{
    [Test, FsCheckProperty]
    public Property Merge_NonemptyValues_ReturnsSharedValueOrUnknown(
        NonEmptyArray<LogicValue> generatedValues)
    {
        var values = generatedValues.Get;
        var expected = values.All(value => value == values[0])
            ? values[0]
            : LogicValue.X;
        var actual = ConservativeMerge.Merge(values);

        return (actual == expected)
            .Label($"expected={expected}; actual={actual}")
            .Collect($"values={values.Length}")
            .Classify(values.All(value => value == values[0]), "all equal");
    }

    [Test]
    public async Task Merge_EmptyValues_ThrowsArgumentException()
    {
        await Assert.That(() => ConservativeMerge.Merge([]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Merge_NullValues_ThrowsArgumentNullException()
    {
        await Assert.That(() => ConservativeMerge.Merge(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Merge_UndefinedValue_ThrowsArgumentOutOfRangeException(
        int undefinedIndex)
    {
        var values = new[] { LogicValue.Zero, LogicValue.One, LogicValue.Zero };
        values[undefinedIndex] = (LogicValue)byte.MaxValue;

        await Assert.That(() => ConservativeMerge.Merge(values))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }
}
