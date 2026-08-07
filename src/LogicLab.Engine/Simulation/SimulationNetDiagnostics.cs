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
        return Canonicalize(InspectDiagnosticFacts(
            artifact,
            driverValues,
            resolutions).Select(fact => CreateDiagnostic(
                fact,
                ir,
                driverValues,
                resolutions,
                sources)));
    }

    public static SimulationDiagnostic[] CreateExact(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions,
        int diagnosticCount)
    {
        var ir = artifact.SimulationIr;
        var sources = artifact.SourceMap.Nets.ToDictionary(
            entry => entry.Ordinal,
            entry => entry.Source);
        var diagnostics = new SimulationDiagnostic[diagnosticCount];
        var index = 0;
        foreach (var fact in InspectDiagnosticFacts(
            artifact,
            driverValues,
            resolutions))
        {
            if (index == diagnostics.Length)
            {
                throw new InvalidOperationException(
                    "The Simulation Diagnostic preflight changed before materialization.");
            }

            diagnostics[index++] = CreateDiagnostic(
                fact,
                ir,
                driverValues,
                resolutions,
                sources);
        }

        if (index != diagnostics.Length)
        {
            throw new InvalidOperationException(
                "The Simulation Diagnostic preflight changed before materialization.");
        }

        Array.Sort(diagnostics, SimulationNetDiagnosticComparer.Instance);
        return diagnostics;
    }

    public static SimulationDiagnosticBufferMeasure MeasureOwnedBuffers(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions)
    {
        var count = 0;
        ulong nestedReferenceSlots = 0;
        foreach (var fact in InspectDiagnosticFacts(
            artifact,
            driverValues,
            resolutions))
        {
            count = checked(count + 1);
            nestedReferenceSlots = checked(
                nestedReferenceSlots + (ulong)ArgumentCount(fact.Kind));
        }

        return new SimulationDiagnosticBufferMeasure(
            count,
            checked((ulong)count + nestedReferenceSlots));
    }

    public static SimulationDiagnostic[] Canonicalize(
        IEnumerable<SimulationDiagnostic> diagnostics)
    {
        return [.. new SortedSet<SimulationDiagnostic>(
            diagnostics,
            SimulationNetDiagnosticComparer.Instance)];
    }

    private static SimulationDiagnostic CreateDiagnostic(
        DiagnosticFact fact,
        SimulationIr ir,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions,
        Dictionary<int, CompilationSource> sources)
    {
        var net = ir.Nets[fact.NetOrdinal];
        var resolution = resolutions[fact.NetOrdinal];
        var primary = sources[fact.NetOrdinal];
        return fact.Kind switch
        {
            SimulationNetDiagnosticKind.Undriven => new SimulationDiagnostic(
                "simulation_net_undriven",
                SimulationDiagnosticSeverity.Warning,
                [],
                primary,
                []),
            SimulationNetDiagnosticKind.UnknownDriver => new SimulationDiagnostic(
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
                []),
            SimulationNetDiagnosticKind.Contention => new SimulationDiagnostic(
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
                []),
            SimulationNetDiagnosticKind.IndeterminateFeedback =>
                new SimulationDiagnostic(
                    "simulation_indeterminate_feedback",
                    SimulationDiagnosticSeverity.Warning,
                    [
                        new SimulationDiagnosticArgument(
                            "unknownCoordinates",
                            new SimulationUnsignedDecimalValue(
                                fact.UnknownCoordinates)),
                    ],
                    primary,
                    []),
            _ => throw new ArgumentOutOfRangeException(
                nameof(fact),
                fact,
                "The Simulation Net Diagnostic kind is undefined."),
        };
    }

    private static IEnumerable<DiagnosticFact> InspectDiagnosticFacts(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions)
    {
        for (var netOrdinal = 0;
            netOrdinal < artifact.SimulationIr.Nets.Count;
            netOrdinal++)
        {
            var resolution = resolutions[netOrdinal];
            if (HasCause(resolution, NetResolutionCauses.Undriven))
            {
                yield return new DiagnosticFact(
                    SimulationNetDiagnosticKind.Undriven,
                    netOrdinal,
                    UnknownCoordinates: 0);
            }

            if (HasCause(resolution, NetResolutionCauses.UnknownDriver))
            {
                yield return new DiagnosticFact(
                    SimulationNetDiagnosticKind.UnknownDriver,
                    netOrdinal,
                    UnknownCoordinates: 0);
            }

            if (HasCause(resolution, NetResolutionCauses.Contention))
            {
                yield return new DiagnosticFact(
                    SimulationNetDiagnosticKind.Contention,
                    netOrdinal,
                    UnknownCoordinates: 0);
            }
        }

        foreach (var feedback in InspectIndeterminateFeedback(
            artifact,
            driverValues,
            resolutions))
        {
            yield return new DiagnosticFact(
                SimulationNetDiagnosticKind.IndeterminateFeedback,
                feedback.PrimaryNetOrdinal,
                feedback.UnknownCoordinates);
        }
    }

    private static int ArgumentCount(SimulationNetDiagnosticKind kind)
    {
        return kind switch
        {
            SimulationNetDiagnosticKind.Undriven => 0,
            SimulationNetDiagnosticKind.UnknownDriver
                or SimulationNetDiagnosticKind.IndeterminateFeedback => 1,
            SimulationNetDiagnosticKind.Contention => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static IEnumerable<IndeterminateFeedback> InspectIndeterminateFeedback(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        VectorNetResolution[] resolutions)
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
            if (unknownCoordinates != 0)
            {
                yield return new IndeterminateFeedback(
                    internalNetOrdinals[0],
                    unknownCoordinates);
            }
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
        for (var bit = 0; bit < resolution.Value.Width; bit++)
        {
            if ((resolution.GetCauses(bit) & cause) != 0)
            {
                return true;
            }
        }

        return false;
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
            if (ContributesToCause(driver, resolution, cause, value))
            {
                count = checked(count + 1);
            }
        }

        return count;
    }

    private static bool ContributesToCause(
        LogicVector driver,
        VectorNetResolution resolution,
        NetResolutionCauses cause,
        LogicValue value)
    {
        for (var bit = 0; bit < driver.Width; bit++)
        {
            if ((resolution.GetCauses(bit) & cause) != 0
                && driver[bit] == value)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct IndeterminateFeedback(
        int PrimaryNetOrdinal,
        ulong UnknownCoordinates);

    private readonly record struct DiagnosticFact(
        SimulationNetDiagnosticKind Kind,
        int NetOrdinal,
        ulong UnknownCoordinates);

    private enum SimulationNetDiagnosticKind
    {
        Undriven,
        UnknownDriver,
        Contention,
        IndeterminateFeedback,
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

internal readonly record struct SimulationDiagnosticBufferMeasure(
    int DiagnosticCount,
    ulong OwnedReferenceSlotCount);
