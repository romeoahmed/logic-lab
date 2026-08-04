using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Components;
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

        AddIndeterminateFeedbackDiagnostics(
            artifact,
            driverValues,
            resolutions,
            sources,
            diagnostics);

        return Canonicalize(diagnostics);
    }

    public static SimulationDiagnostic[] Canonicalize(
        IEnumerable<SimulationDiagnostic> diagnostics)
    {
        return [.. new SortedSet<SimulationDiagnostic>(
            diagnostics,
            SimulationNetDiagnosticComparer.Instance)];
    }

    private static void AddIndeterminateFeedbackDiagnostics(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions,
        Dictionary<int, CompilationSource> netSources,
        List<SimulationDiagnostic> diagnostics)
    {
        var ir = artifact.SimulationIr;
        foreach (var component in ir.StronglyConnectedComponents.Where(
            item => item.IsCyclic))
        {
            var internalDriverOrdinals = component.EvaluatorOrdinals
                .SelectMany(evaluatorOrdinal =>
                    ir.Evaluators[evaluatorOrdinal].OutputDriverOrdinals)
                .Order()
                .ToArray();
            var internalNetOrdinals = internalDriverOrdinals
                .Select(driverOrdinal => ir.Drivers[driverOrdinal].NetOrdinal)
                .OfType<int>()
                .Distinct()
                .Order()
                .ToArray();
            var unknownCoordinates = internalDriverOrdinals.Aggregate(
                0UL,
                (count, driverOrdinal) => checked(
                    count + CountUnknown(driverValues[driverOrdinal])))
                + internalNetOrdinals.Aggregate(
                    0UL,
                    (count, netOrdinal) => checked(
                        count + CountUnknown(resolutions[netOrdinal].Value)));
            if (unknownCoordinates == 0)
            {
                continue;
            }

            var primaryNetOrdinal = internalNetOrdinals[0];
            diagnostics.Add(new SimulationDiagnostic(
                "simulation_indeterminate_feedback",
                SimulationDiagnosticSeverity.Warning,
                [
                    new SimulationDiagnosticArgument(
                        "unknownCoordinates",
                        new SimulationUnsignedDecimalValue(unknownCoordinates)),
                ],
                netSources[primaryNetOrdinal],
                []));
        }
    }

    private static ulong CountUnknown(LogicVector vector)
    {
        ulong count = 0;
        for (var bit = 0; bit < vector.Width; bit++)
        {
            if (vector[bit] == LogicValue.X)
            {
                count = checked(count + 1);
            }
        }

        return count;
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

            var primaryComparison = CompilationSourceComparer.Instance.Compare(
                left.Primary,
                right.Primary);
            if (primaryComparison != 0)
            {
                return primaryComparison;
            }

            var codeComparison = string.CompareOrdinal(left.Code, right.Code);
            if (codeComparison != 0)
            {
                return codeComparison;
            }

            var argumentsComparison = CompareArguments(left.Arguments, right.Arguments);
            return argumentsComparison != 0
                ? argumentsComparison
                : CompareSourceCollections(left.Related, right.Related);
        }

        private static int CompareArguments(
            ReadOnlyCollection<SimulationDiagnosticArgument> left,
            ReadOnlyCollection<SimulationDiagnosticArgument> right)
        {
            var commonCount = Math.Min(left.Count, right.Count);
            for (var index = 0; index < commonCount; index++)
            {
                var nameComparison = string.CompareOrdinal(
                    left[index].Name,
                    right[index].Name);
                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                var valueComparison = CompareValues(
                    left[index].Value,
                    right[index].Value);
                if (valueComparison != 0)
                {
                    return valueComparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private static int CompareValues(
            SimulationDiagnosticValue left,
            SimulationDiagnosticValue right)
        {
            var kindComparison = ValueKind(left).CompareTo(ValueKind(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (SimulationStableTokenValue l, SimulationStableTokenValue r) =>
                    string.CompareOrdinal(l.Value, r.Value),
                (SimulationUnsignedDecimalValue l, SimulationUnsignedDecimalValue r) =>
                    l.Value.CompareTo(r.Value),
                (SimulationLogicValue l, SimulationLogicValue r) =>
                    l.Value.CompareTo(r.Value),
                (SimulationContractKeyValue l, SimulationContractKeyValue r) =>
                    CompareContractKeys(l.Value, r.Value),
                (SimulationCorrelationTokenValue l,
                    SimulationCorrelationTokenValue r) =>
                    string.CompareOrdinal(l.Value, r.Value),
                _ => throw new InvalidOperationException(
                    "The Simulation Diagnostic Value variant is undefined."),
            };
        }

        private static int CompareSourceCollections(
            ReadOnlyCollection<CompilationSource> left,
            ReadOnlyCollection<CompilationSource> right)
        {
            var commonCount = Math.Min(left.Count, right.Count);
            for (var index = 0; index < commonCount; index++)
            {
                var comparison = CompilationSourceComparer.Instance.Compare(
                    left[index],
                    right[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private static int CompareContractKeys(
            ComponentContractKey left,
            ComponentContractKey right)
        {
            var libraryComparison = string.CompareOrdinal(
                left.LibraryId,
                right.LibraryId);
            return libraryComparison != 0
                ? libraryComparison
                : string.CompareOrdinal(left.ContractId, right.ContractId);
        }

        private static int ValueKind(SimulationDiagnosticValue value)
        {
            return value switch
            {
                SimulationStableTokenValue => 0,
                SimulationUnsignedDecimalValue => 1,
                SimulationLogicValue => 2,
                SimulationContractKeyValue => 3,
                SimulationCorrelationTokenValue => 4,
                _ => throw new InvalidOperationException(
                    "The Simulation Diagnostic Value variant is undefined."),
            };
        }
    }
}
