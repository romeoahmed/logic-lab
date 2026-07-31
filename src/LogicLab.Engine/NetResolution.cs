using LogicLab.Domain;

namespace LogicLab.Engine;

[Flags]
public enum NetResolutionCauses
{
    None = 0,
    Undriven = 1 << 0,
    UnknownDriver = 1 << 1,
    Contention = 1 << 2,
}

public readonly record struct NetResolution(
    LogicValue Value,
    NetResolutionCauses Causes);
