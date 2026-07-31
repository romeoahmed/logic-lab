using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class VectorNetResolverTests
{
    [Fact]
    public void Resolve_ArbitraryDriverSets_MatchesScalarValueAndCausesAtEveryBit()
    {
        Prop.ForAll<int[]>(data =>
            {
                var seed = data is { Length: > 0 } ? data[0] : 0;
                var width = LogicVectorTestData.PositiveWidth(seed);
                var countSeed = data is { Length: > 1 } ? data[1] : seed;
                var driverCount = (int)(unchecked((uint)countSeed) % 6u);
                var driverValues = Enumerable.Range(0, driverCount)
                    .Select(index => LogicVectorTestData.CreateValues(
                        width,
                        unchecked(seed ^ (index * 1_000_003)),
                        data?.Select(value => unchecked(value + index)).ToArray()))
                    .ToArray();
                var drivers = driverValues
                    .Select(values => new LogicVector(values))
                    .ToArray();

                var actual = VectorNetResolver.Resolve(width, drivers);

                return Enumerable.Range(0, width).All(bitIndex =>
                {
                    var expected = NetResolver.Resolve(
                        driverValues
                            .Select(values => values[bitIndex])
                            .ToArray());
                    return actual.Value[bitIndex] == expected.Value
                        && actual.GetCauses(bitIndex) == expected.Causes;
                });
            })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void Resolve_NoDrivers_ReturnsHighImpedanceAndUndrivenForEveryBit()
    {
        var actual = VectorNetResolver.Resolve(130, []);

        Assert.Equal(130, actual.Value.Width);
        for (var index = 0; index < actual.Value.Width; index++)
        {
            Assert.Equal(LogicValue.Z, actual.Value[index]);
            Assert.Equal(
                NetResolutionCauses.Undriven,
                actual.GetCauses(index));
        }
    }

    [Fact]
    public void Resolve_AllHighImpedanceDrivers_ReturnsUndrivenForEveryBit()
    {
        var highImpedance = new LogicVector(
            Enumerable.Repeat(LogicValue.Z, 130).ToArray());

        var actual = VectorNetResolver.Resolve(
            130,
            [highImpedance, highImpedance]);

        for (var index = 0; index < actual.Value.Width; index++)
        {
            Assert.Equal(LogicValue.Z, actual.Value[index]);
            Assert.Equal(
                NetResolutionCauses.Undriven,
                actual.GetCauses(index));
        }
    }

    [Fact]
    public void Resolve_MultipleDriversAtWordTails_ReportIndependentValuesAndCauses()
    {
        const int width = 130;
        var firstValues = Enumerable.Repeat(LogicValue.Z, width).ToArray();
        var secondValues = Enumerable.Repeat(LogicValue.Z, width).ToArray();
        var thirdValues = Enumerable.Repeat(LogicValue.Z, width).ToArray();
        firstValues[1] = LogicValue.One;
        secondValues[1] = LogicValue.One;
        firstValues[63] = LogicValue.X;
        firstValues[64] = LogicValue.Zero;
        secondValues[64] = LogicValue.One;
        firstValues[129] = LogicValue.Zero;
        secondValues[129] = LogicValue.One;
        thirdValues[129] = LogicValue.X;

        var actual = VectorNetResolver.Resolve(
            width,
            [
                new LogicVector(firstValues),
                new LogicVector(secondValues),
                new LogicVector(thirdValues),
            ]);

        Assert.Equal(LogicValue.Z, actual.Value[0]);
        Assert.Equal(NetResolutionCauses.Undriven, actual.GetCauses(0));
        Assert.Equal(LogicValue.One, actual.Value[1]);
        Assert.Equal(NetResolutionCauses.None, actual.GetCauses(1));
        Assert.Equal(LogicValue.X, actual.Value[63]);
        Assert.Equal(
            NetResolutionCauses.UnknownDriver,
            actual.GetCauses(63));
        Assert.Equal(LogicValue.X, actual.Value[64]);
        Assert.Equal(
            NetResolutionCauses.Contention,
            actual.GetCauses(64));
        Assert.Equal(LogicValue.X, actual.Value[129]);
        Assert.Equal(
            NetResolutionCauses.UnknownDriver | NetResolutionCauses.Contention,
            actual.GetCauses(129));
    }

    [Fact]
    public void Resolve_DifferentDriverWidth_ThrowsArgumentException()
    {
        var driver = new LogicVector([LogicValue.Zero]);

        Assert.Throws<ArgumentException>(
            () => VectorNetResolver.Resolve(2, [driver]));
    }

    [Fact]
    public void Resolve_NullDrivers_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => VectorNetResolver.Resolve(1, null!));
    }

    [Fact]
    public void Resolve_NullDriverElement_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => VectorNetResolver.Resolve(1, [null!]));
    }

    [Fact]
    public void Resolve_NonpositiveWidth_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VectorNetResolver.Resolve(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VectorNetResolver.Resolve(-1, []));
    }

    [Fact]
    public void GetCauses_IndexOutsideVector_ThrowsArgumentOutOfRangeException()
    {
        var resolution = VectorNetResolver.Resolve(1, []);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => resolution.GetCauses(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => resolution.GetCauses(1));
    }
}
