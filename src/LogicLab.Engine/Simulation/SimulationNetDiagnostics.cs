using LogicLab.Domain;
using LogicLab.Domain.Authoring;
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

        diagnostics.Sort(SimulationNetDiagnosticComparer.Instance);
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

    private sealed class SimulationNetDiagnosticComparer : IComparer<SimulationDiagnostic>
    {
        public static SimulationNetDiagnosticComparer Instance { get; } = new();

        public int Compare(SimulationDiagnostic? left, SimulationDiagnostic? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var primaryComparison = CompareSources(left.Primary, right.Primary);
            return primaryComparison != 0
                ? primaryComparison
                : string.CompareOrdinal(left.Code, right.Code);
        }

        private static int CompareSources(
            CompilationSource? left,
            CompilationSource? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var leftNet = (NetSourceIdentity)left.Identity;
            var rightNet = (NetSourceIdentity)right.Identity;
            var circuitComparison = string.CompareOrdinal(
                leftNet.CircuitDefinitionId.Value,
                rightNet.CircuitDefinitionId.Value);
            if (circuitComparison != 0)
            {
                return circuitComparison;
            }

            var pathComparison = ComparePaths(left.HierarchyPath, right.HierarchyPath);
            return pathComparison != 0
                ? pathComparison
                : string.CompareOrdinal(leftNet.NetId.Value, rightNet.NetId.Value);
        }

        private static int ComparePaths(HierarchyPath left, HierarchyPath right)
        {
            var entryComparison = string.CompareOrdinal(
                left.EntryCircuitDefinitionId.Value,
                right.EntryCircuitDefinitionId.Value);
            if (entryComparison != 0)
            {
                return entryComparison;
            }

            var commonCount = Math.Min(left.Steps.Count, right.Steps.Count);
            for (var index = 0; index < commonCount; index++)
            {
                var definitionComparison = string.CompareOrdinal(
                    left.Steps[index].ContainingCircuitDefinitionId.Value,
                    right.Steps[index].ContainingCircuitDefinitionId.Value);
                if (definitionComparison != 0)
                {
                    return definitionComparison;
                }

                var instanceComparison = string.CompareOrdinal(
                    left.Steps[index].ComponentInstanceId.Value,
                    right.Steps[index].ComponentInstanceId.Value);
                if (instanceComparison != 0)
                {
                    return instanceComparison;
                }
            }

            return left.Steps.Count.CompareTo(right.Steps.Count);
        }
    }
}
