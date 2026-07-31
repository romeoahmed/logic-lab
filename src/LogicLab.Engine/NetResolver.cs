using LogicLab.Domain;

namespace LogicLab.Engine;

public static class NetResolver
{
    public static NetResolution Resolve(IReadOnlyList<LogicValue> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        var sawZero = false;
        var sawOne = false;
        var sawUnknown = false;

        foreach (var driver in drivers)
        {
            ScalarLogic.EnsureDefined(driver, nameof(drivers));

            switch (driver)
            {
                case LogicValue.Zero:
                    sawZero = true;
                    break;
                case LogicValue.One:
                    sawOne = true;
                    break;
                case LogicValue.X:
                    sawUnknown = true;
                    break;
                case LogicValue.Z:
                    break;
            }
        }

        if (!sawZero && !sawOne && !sawUnknown)
        {
            return new NetResolution(
                LogicValue.Z,
                NetResolutionCauses.Undriven);
        }

        var hasContention = sawZero && sawOne;
        var causes = NetResolutionCauses.None;

        if (sawUnknown)
        {
            causes |= NetResolutionCauses.UnknownDriver;
        }

        if (hasContention)
        {
            causes |= NetResolutionCauses.Contention;
        }

        if (sawUnknown || hasContention)
        {
            return new NetResolution(LogicValue.X, causes);
        }

        return new NetResolution(
            sawOne ? LogicValue.One : LogicValue.Zero,
            causes);
    }
}
