using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal static class SimulationNetDiagnostics
{
    public static SimulationDiagnostic[] Create(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions)
    {
        var ir = artifact.SimulationIr;
        var sources = artifact.SourceMap.Nets.ToDictionary(
            entry => entry.Ordinal,
            entry => entry.Source);
        var diagnostics = new List<SimulationDiagnostic>();
        for (var netOrdinal = 0; netOrdinal < ir.Nets.Count; netOrdinal++)
        {
            var net = ir.Nets[netOrdinal];
            var resolution = resolutions[netOrdinal];
            var primary = sources[netOrdinal];
            if (HasCause(resolution, NetResolutionCauses.Undriven))
            {
                diagnostics.Add(new SimulationDiagnostic(
                    "simulation_net_undriven",
                    SimulationDiagnosticSeverity.Warning,
                    [],
                    primary,
                    []));
            }

            if (HasCause(resolution, NetResolutionCauses.UnknownDriver))
            {
                diagnostics.Add(new SimulationDiagnostic(
                    "simulation_unknown_driver",
                    SimulationDiagnosticSeverity.Warning,
                    [
                        new SimulationDiagnosticArgument(
                            "driverCount",
                            new SimulationUnsignedDecimalValue(CountDrivers(
                                net,
                                driverValues,
                                resolution,
                                NetResolutionCauses.UnknownDriver,
                                LogicValue.X))),
                    ],
                    primary,
                    []));
            }

            if (HasCause(resolution, NetResolutionCauses.Contention))
            {
                diagnostics.Add(new SimulationDiagnostic(
                    "simulation_contention",
                    SimulationDiagnosticSeverity.Error,
                    [
                        new SimulationDiagnosticArgument(
                            "zeroDrivers",
                            new SimulationUnsignedDecimalValue(CountDrivers(
                                net,
                                driverValues,
                                resolution,
                                NetResolutionCauses.Contention,
                                LogicValue.Zero))),
                        new SimulationDiagnosticArgument(
                            "oneDrivers",
                            new SimulationUnsignedDecimalValue(CountDrivers(
                                net,
                                driverValues,
                                resolution,
                                NetResolutionCauses.Contention,
                                LogicValue.One))),
                        new SimulationDiagnosticArgument(
                            "unknownDrivers",
                            new SimulationUnsignedDecimalValue(CountDrivers(
                                net,
                                driverValues,
                                resolution,
                                NetResolutionCauses.Contention,
                                LogicValue.X))),
                    ],
                    primary,
                    []));
            }
        }

        return diagnostics.ToArray();
    }

    private static bool HasCause(
        VectorNetResolution resolution,
        NetResolutionCauses cause)
    {
        return Enumerable.Range(0, resolution.Value.Width)
            .Any(bit => (resolution.GetCauses(bit) & cause) != 0);
    }

    private static ulong CountDrivers(
        SimulationNet net,
        LogicVector[] driverValues,
        VectorNetResolution resolution,
        NetResolutionCauses cause,
        LogicValue value)
    {
        ulong count = 0;
        foreach (var driverOrdinal in net.DriverOrdinals)
        {
            var driver = driverValues[driverOrdinal];
            if (Enumerable.Range(0, driver.Width).Any(bit =>
                    (resolution.GetCauses(bit) & cause) != 0
                    && driver[bit] == value))
            {
                count = checked(count + 1);
            }
        }

        return count;
    }
}
