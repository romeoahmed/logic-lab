using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class ConservativeMergeTests
{
    [Test]
    [Arguments(LogicValue.Zero)]
    [Arguments(LogicValue.One)]
    [Arguments(LogicValue.X)]
    [Arguments(LogicValue.Z)]
    public async Task Merge_IdenticalValues_ReturnsSharedValue(LogicValue value)
    {
        var actual = ConservativeMerge.Merge([value, value, value]);

        await Assert.That(actual).IsEqualTo(value);
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.One)]
    [Arguments(LogicValue.Zero, LogicValue.X)]
    [Arguments(LogicValue.Zero, LogicValue.Z)]
    [Arguments(LogicValue.One, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.X)]
    [Arguments(LogicValue.One, LogicValue.Z)]
    [Arguments(LogicValue.X, LogicValue.Zero)]
    [Arguments(LogicValue.X, LogicValue.One)]
    [Arguments(LogicValue.X, LogicValue.Z)]
    [Arguments(LogicValue.Z, LogicValue.Zero)]
    [Arguments(LogicValue.Z, LogicValue.One)]
    [Arguments(LogicValue.Z, LogicValue.X)]
    public async Task Merge_DifferentValues_ReturnsUnknown(
        LogicValue first,
        LogicValue second)
    {
        var actual = ConservativeMerge.Merge([first, second]);

        await Assert.That(actual).IsEqualTo(LogicValue.X);
    }

    [Test]
    public async Task Merge_EmptyValues_ThrowsArgumentException()
    {
        await Assert.That(() => ConservativeMerge.Merge([]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Merge_NullValues_ThrowsArgumentNullException()
    {
        await Assert.That(() => ConservativeMerge.Merge(null!))
            .Throws<ArgumentNullException>();
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
            .Throws<ArgumentOutOfRangeException>();
    }
}
