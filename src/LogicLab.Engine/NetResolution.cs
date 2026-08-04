using LogicLab.Domain;

namespace LogicLab.Engine;

[Flags]
internal enum NetResolutionCauses
{
    None = 0,
    Undriven = 1 << 0,
    UnknownDriver = 1 << 1,
    Contention = 1 << 2,
}

internal readonly record struct NetResolution(
    LogicValue Value,
    NetResolutionCauses Causes);
