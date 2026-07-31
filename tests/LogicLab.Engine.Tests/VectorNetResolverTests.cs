using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class VectorNetResolverTests
{
    [Test, FsCheckProperty]
    public Property Resolve_ArbitraryDriverSets_MatchesScalarValueAndCausesAtEveryBit()
    {
        return Prop.ForAll<int[]>(data =>
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
        });
    }

    [Test]
    public async Task Resolve_NoDrivers_ReturnsHighImpedanceAndUndrivenForEveryBit()
    {
        var actual = VectorNetResolver.Resolve(130, []);
        var actualResolutions = ToScalarResolutions(actual);
        var expectedResolutions = Enumerable.Repeat(
                new NetResolution(LogicValue.Z, NetResolutionCauses.Undriven),
                130)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(actual.Value.Width).IsEqualTo(130);
            await Assert.That(actualResolutions)
                .IsEquivalentTo(expectedResolutions, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Resolve_AllHighImpedanceDrivers_ReturnsUndrivenForEveryBit()
    {
        var highImpedance = new LogicVector(
            Enumerable.Repeat(LogicValue.Z, 130).ToArray());

        var actual = VectorNetResolver.Resolve(
            130,
            [highImpedance, highImpedance]);
        var actualResolutions = ToScalarResolutions(actual);
        var expectedResolutions = Enumerable.Repeat(
                new NetResolution(LogicValue.Z, NetResolutionCauses.Undriven),
                130)
            .ToArray();

        await Assert.That(actualResolutions)
            .IsEquivalentTo(expectedResolutions, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Resolve_MultipleDriversAtWordTails_ReportIndependentValuesAndCauses()
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

        using (Assert.Multiple())
        {
            await Assert.That(actual.Value[0]).IsEqualTo(LogicValue.Z);
            await Assert.That(actual.GetCauses(0)).IsEqualTo(NetResolutionCauses.Undriven);
            await Assert.That(actual.Value[1]).IsEqualTo(LogicValue.One);
            await Assert.That(actual.GetCauses(1)).IsEqualTo(NetResolutionCauses.None);
            await Assert.That(actual.Value[63]).IsEqualTo(LogicValue.X);
            await Assert.That(actual.GetCauses(63))
                .IsEqualTo(NetResolutionCauses.UnknownDriver);
            await Assert.That(actual.Value[64]).IsEqualTo(LogicValue.X);
            await Assert.That(actual.GetCauses(64))
                .IsEqualTo(NetResolutionCauses.Contention);
            await Assert.That(actual.Value[129]).IsEqualTo(LogicValue.X);
            await Assert.That(actual.GetCauses(129))
                .IsEqualTo(
                    NetResolutionCauses.UnknownDriver | NetResolutionCauses.Contention);
        }
    }

    [Test]
    public async Task Resolve_ListDriversAcrossWordBoundary_MatchesScalarValueAndCausesAtEveryBit()
    {
        const int width = 130;
        var firstValues = Enumerable.Range(0, width)
            .Select(index => (LogicValue)(index & 3))
            .ToArray();
        var secondValues = Enumerable.Range(0, width)
            .Select(index => (LogicValue)((index + 1) & 3))
            .ToArray();
        var thirdValues = Enumerable.Range(0, width)
            .Select(index => (LogicValue)((index + 2) & 3))
            .ToArray();
        firstValues[63] = LogicValue.X;
        secondValues[63] = LogicValue.Z;
        thirdValues[63] = LogicValue.Z;
        firstValues[64] = LogicValue.Zero;
        secondValues[64] = LogicValue.One;
        thirdValues[64] = LogicValue.Z;
        firstValues[129] = LogicValue.Zero;
        secondValues[129] = LogicValue.One;
        thirdValues[129] = LogicValue.X;
        List<LogicVector> drivers =
        [
            new LogicVector(firstValues),
            new LogicVector(secondValues),
            new LogicVector(thirdValues),
        ];

        var actual = VectorNetResolver.Resolve(width, drivers);
        var actualResolutions = ToScalarResolutions(actual);
        var expectedResolutions = Enumerable.Range(0, width)
            .Select(bitIndex => NetResolver.Resolve(
                [
                    firstValues[bitIndex],
                    secondValues[bitIndex],
                    thirdValues[bitIndex],
                ]))
            .ToArray();

        await Assert.That(actualResolutions)
            .IsEquivalentTo(expectedResolutions, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Resolve_DifferentDriverWidth_ThrowsArgumentException()
    {
        var driver = new LogicVector([LogicValue.Zero]);

        await Assert.That(() => VectorNetResolver.Resolve(2, [driver]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Resolve_NullDrivers_ThrowsArgumentNullException()
    {
        await Assert.That(() => VectorNetResolver.Resolve(1, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Resolve_NullDriverElement_ThrowsArgumentException()
    {
        await Assert.That(() => VectorNetResolver.Resolve(1, [null!]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Resolve_NonpositiveWidth_ThrowsArgumentOutOfRangeException()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => VectorNetResolver.Resolve(0, []))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => VectorNetResolver.Resolve(-1, []))
                .ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GetCauses_IndexOutsideVector_ThrowsArgumentOutOfRangeException()
    {
        var resolution = VectorNetResolver.Resolve(1, []);

        using (Assert.Multiple())
        {
            await Assert.That(() => resolution.GetCauses(-1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => resolution.GetCauses(1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }

    private static NetResolution[] ToScalarResolutions(VectorNetResolution resolution)
    {
        return Enumerable.Range(0, resolution.Value.Width)
            .Select(bitIndex => new NetResolution(
                resolution.Value[bitIndex],
                resolution.GetCauses(bitIndex)))
            .ToArray();
    }
}
