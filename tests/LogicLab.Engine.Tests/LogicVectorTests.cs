using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class LogicVectorTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Create_ValidVector_RoundTripsEveryValueInBitIndexOrder(
        LogicVectorCase sample)
    {
        var vector = new LogicVector(sample.Values);
        var matches = LogicVectorTestData.Matches(vector, sample.Values);

        return matches
            .Label(LogicVectorTestData.MismatchLabel(vector, sample.Values))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Create_ValidVector_OwnsInputValues(LogicVectorCase sample)
    {
        var input = sample.Values.ToArray();
        var expected = input.ToArray();
        var vector = new LogicVector(input);

        input[0] = input[0] == LogicValue.Zero
            ? LogicValue.One
            : LogicValue.Zero;

        return LogicVectorTestData.Matches(vector, expected)
            .Label("vector owns its input value sequence")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    [Test]
    public async Task Create_EmptyValues_ThrowsArgumentException()
    {
        await Assert.That(() => new LogicVector([]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Create_NullValues_ThrowsArgumentNullException()
    {
        await Assert.That(() => new LogicVector(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Create_UndefinedLogicValue_ThrowsArgumentOutOfRangeException(
        int undefinedIndex)
    {
        var values = new[] { LogicValue.Zero, LogicValue.One, LogicValue.Z };
        values[undefinedIndex] = (LogicValue)byte.MaxValue;

        await Assert.That(() => new LogicVector(values))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Indexer_IndexOutsideVector_ThrowsArgumentOutOfRangeException()
    {
        var vector = new LogicVector([LogicValue.Zero]);

        using (Assert.Multiple())
        {
            await Assert.That(() => vector[-1]).ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => vector[1]).ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }
}
