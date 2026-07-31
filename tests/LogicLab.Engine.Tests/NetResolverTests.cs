using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class NetResolverTests
{
    [Fact]
    public void Resolve_NoEffectiveDriver_ReturnsHighImpedanceWithUndrivenCause()
    {
        var noDrivers = NetResolver.Resolve([]);
        var highImpedanceDrivers = NetResolver.Resolve([LogicValue.Z, LogicValue.Z]);

        Assert.Equal(
            new NetResolution(LogicValue.Z, NetResolutionCauses.Undriven),
            noDrivers);
        Assert.Equal(
            new NetResolution(LogicValue.Z, NetResolutionCauses.Undriven),
            highImpedanceDrivers);
    }

    [Theory]
    [InlineData(LogicValue.Zero)]
    [InlineData(LogicValue.One)]
    public void Resolve_EqualKnownDrivers_ReturnsKnownValueWithoutCause(
        LogicValue value)
    {
        var actual = NetResolver.Resolve([LogicValue.Z, value, value]);

        Assert.Equal(
            new NetResolution(value, NetResolutionCauses.None),
            actual);
    }

    [Fact]
    public void Resolve_UnknownDriver_ReturnsUnknownWithUnknownDriverCause()
    {
        var actual = NetResolver.Resolve(
            [LogicValue.Z, LogicValue.Zero, LogicValue.X]);

        Assert.Equal(
            new NetResolution(LogicValue.X, NetResolutionCauses.UnknownDriver),
            actual);
    }

    [Fact]
    public void Resolve_ConflictingKnownDrivers_ReturnsUnknownWithContentionCause()
    {
        var actual = NetResolver.Resolve(
            [LogicValue.Z, LogicValue.Zero, LogicValue.One]);

        Assert.Equal(
            new NetResolution(LogicValue.X, NetResolutionCauses.Contention),
            actual);
    }

    [Fact]
    public void Resolve_UnknownAndConflictingDrivers_ReturnsBothCauses()
    {
        var actual = NetResolver.Resolve(
            [LogicValue.Zero, LogicValue.X, LogicValue.One, LogicValue.Z]);

        Assert.Equal(
            new NetResolution(
                LogicValue.X,
                NetResolutionCauses.UnknownDriver | NetResolutionCauses.Contention),
            actual);
    }

    [Fact]
    public void Resolve_NullDrivers_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NetResolver.Resolve(null!));
    }

    [Fact]
    public void Resolve_UndefinedDriver_ThrowsArgumentOutOfRangeException()
    {
        var undefined = (LogicValue)byte.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NetResolver.Resolve([LogicValue.Z, undefined]));
    }
}
