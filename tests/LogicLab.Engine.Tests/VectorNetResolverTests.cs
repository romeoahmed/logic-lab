using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class VectorNetResolverTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Resolve_ValidDriverSets_MatchesScalarValueAndCausesAtEveryBit(
        LogicVectorDriverCase sample)
    {
        var drivers = sample.Drivers
            .Select(values => new LogicVector(values))
            .ToArray();
        var arrayResolution = VectorNetResolver.Resolve(sample.Width, drivers);
        var listResolution = VectorNetResolver.Resolve(sample.Width, [.. drivers]);
        var matches = true;
        var label = "array and list carriers match the scalar oracle";

        for (var bitIndex = 0; bitIndex < sample.Width; bitIndex++)
        {
            var expected = NetResolver.Resolve(
                [.. sample.Drivers.Select(values => values[bitIndex])]);
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

}
