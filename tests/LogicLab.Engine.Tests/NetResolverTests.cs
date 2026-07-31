using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class NetResolverTests
{
    [Test]
    public async Task Resolve_NoEffectiveDriver_ReturnsHighImpedanceWithUndrivenCause()
    {
        var noDrivers = NetResolver.Resolve([]);
        var highImpedanceDrivers = NetResolver.Resolve([LogicValue.Z, LogicValue.Z]);
        var expected = new NetResolution(LogicValue.Z, NetResolutionCauses.Undriven);

        using (Assert.Multiple())
        {
            await Assert.That(noDrivers).IsEqualTo(expected);
            await Assert.That(highImpedanceDrivers).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments(LogicValue.Zero)]
    [Arguments(LogicValue.One)]
    public async Task Resolve_EqualKnownDrivers_ReturnsKnownValueWithoutCause(
        LogicValue value)
    {
        var actual = NetResolver.Resolve([LogicValue.Z, value, value]);

        await Assert.That(actual)
            .IsEqualTo(new NetResolution(value, NetResolutionCauses.None));
    }

    [Test]
    public async Task Resolve_UnknownDriver_ReturnsUnknownWithUnknownDriverCause()
    {
        var actual = NetResolver.Resolve(
            [LogicValue.Z, LogicValue.Zero, LogicValue.X]);

        await Assert.That(actual)
            .IsEqualTo(
                new NetResolution(LogicValue.X, NetResolutionCauses.UnknownDriver));
    }

    [Test]
    public async Task Resolve_ConflictingKnownDrivers_ReturnsUnknownWithContentionCause()
    {
        var actual = NetResolver.Resolve(
            [LogicValue.Z, LogicValue.Zero, LogicValue.One]);

        await Assert.That(actual)
            .IsEqualTo(new NetResolution(LogicValue.X, NetResolutionCauses.Contention));
    }

    [Test]
    public async Task Resolve_UnknownAndConflictingDrivers_ReturnsBothCauses()
    {
        var actual = NetResolver.Resolve(
            [LogicValue.Zero, LogicValue.X, LogicValue.One, LogicValue.Z]);

        await Assert.That(actual)
            .IsEqualTo(
                new NetResolution(
                    LogicValue.X,
                    NetResolutionCauses.UnknownDriver | NetResolutionCauses.Contention));
    }

    [Test]
    public async Task Resolve_NullDrivers_ThrowsArgumentNullException()
    {
        await Assert.That(() => NetResolver.Resolve(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Resolve_UndefinedDriver_ThrowsArgumentOutOfRangeException()
    {
        var undefined = (LogicValue)byte.MaxValue;

        await Assert.That(() => NetResolver.Resolve([LogicValue.Z, undefined]))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }
}
