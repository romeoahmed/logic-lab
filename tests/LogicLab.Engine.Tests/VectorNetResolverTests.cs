using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class VectorNetResolverTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Resolve_ValidDriverSets_MatchesScalarValueAndCausesAtEveryBit(
        LogicVectorDriverCase sample)
    {
        var drivers = sample.Drivers
            .Select(values => new LogicVector(values))
            .ToArray();
        var arrayResolution = VectorNetResolver.Resolve(sample.Width, drivers);
        var listResolution = VectorNetResolver.Resolve(sample.Width, drivers.ToList());
        var matches = true;
        var label = "array and list carriers match the scalar oracle";

        for (var bitIndex = 0; bitIndex < sample.Width; bitIndex++)
        {
            var expected = NetResolver.Resolve(
                sample.Drivers
                    .Select(values => values[bitIndex])
                    .ToArray());
            var arrayValue = arrayResolution.Value[bitIndex];
            var arrayCauses = arrayResolution.GetCauses(bitIndex);
            var listValue = listResolution.Value[bitIndex];
            var listCauses = listResolution.GetCauses(bitIndex);
            if (arrayValue != expected.Value
                || arrayCauses != expected.Causes
                || listValue != expected.Value
                || listCauses != expected.Causes)
            {
                matches = false;
                label = $"bit {bitIndex}: expected {expected}, "
                    + $"array {arrayValue}/{arrayCauses}, list {listValue}/{listCauses}";
                break;
            }
        }

        return matches
            .Label(label)
            .Collect(LogicVectorTestData.WidthBucket(sample.Width))
            .Collect($"drivers={sample.Drivers.Length}");
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
