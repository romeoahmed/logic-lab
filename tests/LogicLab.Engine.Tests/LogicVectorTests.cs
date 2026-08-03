using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class LogicVectorTests
{
    [Test, FsCheckProperty]
    public Property Create_ArbitraryPositiveWidth_RoundTripsEveryValueInBitIndexOrder()
    {
        return Prop.ForAll<int[]>(data =>
        {
            var widthSeed = data is { Length: > 0 } ? data[0] : 0;
            var width = LogicVectorTestData.PositiveWidth(widthSeed);
            var values = LogicVectorTestData.CreateValues(
                width,
                widthSeed,
                data);
            var vector = new LogicVector(values);

            return LogicVectorTestData.Matches(vector, values);
        });
    }

    [Test]
    [Arguments(1)]
    [Arguments(63)]
    [Arguments(64)]
    [Arguments(65)]
    [Arguments(127)]
    [Arguments(128)]
    [Arguments(129)]
    public async Task Create_WordBoundaryWidth_RoundTripsEveryLogicValue(int width)
    {
        var values = Enumerable.Range(0, width)
            .Select(index => (LogicValue)(index % 4))
            .ToArray();

        var vector = new LogicVector(values);
        var actual = LogicVectorTestData.ToValues(vector);

        using (Assert.Multiple())
        {
            await Assert.That(vector.Width).IsEqualTo(width);
            await Assert.That(actual)
                .IsEquivalentTo(values, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Create_SourceMutation_DoesNotChangeOwnedVector()
    {
        var values = new[]
        {
            LogicValue.Zero,
            LogicValue.One,
            LogicValue.X,
            LogicValue.Z,
        };
        var vector = new LogicVector(values);

        Array.Fill(values, LogicValue.One);

        var actual = LogicVectorTestData.ToValues(vector);
        await Assert.That(actual)
            .IsEquivalentTo(
                [LogicValue.Zero, LogicValue.One, LogicValue.X, LogicValue.Z],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task Create_ChangingInput_UsesSingleSnapshot()
    {
        LogicValue[] values = [LogicValue.Zero, LogicValue.One];
        var vector = new LogicVector(
            new ChangingReadOnlyList<LogicValue>(0, values));

        await Assert.That(LogicVectorTestData.ToValues(vector))
            .IsEquivalentTo(values, CollectionOrdering.Matching);
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
