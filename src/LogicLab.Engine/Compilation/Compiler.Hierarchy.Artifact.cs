using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

public static partial class Compiler
{
    private static CompilationArtifact BuildHierarchyArtifact(
        CompilationRequest request,
        HierarchyResolvedInstance[] resolvedInstances,
        HierarchyTopology topology,
        CancellationToken cancellationToken)
    {
        var resolvedByOccurrenceAndId = resolvedInstances.ToDictionary(
            instance => new HierarchyInstanceKey(
                instance.Occurrence,
                instance.Instance.Id));
        var runtimeNetByTerminal = topology.ScopedNetByTerminal.ToDictionary(
            item => item.Key,
            item => topology.RuntimeNetByScopedNet[item.Value]);
        var (drivers, driverSources, driverByInstancePort) = BuildHierarchyDrivers(
            resolvedInstances,
            runtimeNetByTerminal,
            cancellationToken);
        var (evaluators, evaluatorSources, evaluatorInputSources) =
            BuildHierarchyEvaluators(
                resolvedInstances,
                runtimeNetByTerminal,
                driverByInstancePort,
                cancellationToken);
        var (simulationNets, netSources, netAliases) = BuildHierarchyNets(
            topology,
            resolvedByOccurrenceAndId,
            driverByInstancePort,
            cancellationToken);
        var (fanoutOffsets, fanoutEvaluators) = BuildFanout(
            simulationNets,
            cancellationToken);
        var adjacency = BuildEvaluatorAdjacency(
            evaluators,
            drivers,
            simulationNets,
            cancellationToken);
        var graphPlan = CompilerGraph.CreatePlan(adjacency, cancellationToken);
        var simulationIr = new SimulationIr(
            evaluators,
            drivers,
            simulationNets,
            fanoutOffsets,
            fanoutEvaluators,
            graphPlan.Components,
            graphPlan.CondensationOrder);
        var sccMemberSources = graphPlan.Components
            .SelectMany(component => component.EvaluatorOrdinals.Select(
                evaluatorOrdinal => new StronglyConnectedComponentMemberSourceMapEntry(
                    component.Ordinal,
                    evaluatorOrdinal,
                    evaluatorSources[evaluatorOrdinal].Source)))
            .ToArray();
        var sourceMap = new SourceMap(
            evaluatorSources,
            evaluatorInputSources,
            driverSources,
            netSources,
            sccMemberSources,
            netAliases);
        CompilationArtifactValidator.Validate(simulationIr, sourceMap, cancellationToken);
        var key = new CompilationArtifactKey(
            request.ProjectRevision.RevisionId,
            request.EntryCircuitDefinitionId,
            request.LibrarySnapshot.Fingerprint,
            SemanticVersion);
        return new CompilationArtifact(key, simulationIr, sourceMap, request.ProjectRevision);
    }

    private static (
        SimulationDriver[] Drivers,
        SourceMapEntry[] Sources,
        Dictionary<HierarchyInstancePortKey, int> OrdinalByInstancePort)
        BuildHierarchyDrivers(
            IEnumerable<HierarchyResolvedInstance> resolvedInstances,
            IReadOnlyDictionary<HierarchyTerminalKey, int> runtimeNetByTerminal,
            CancellationToken cancellationToken)
    {
        var drivers = new List<SimulationDriver>();
        var sources = new List<SourceMapEntry>();
        var ordinalByInstancePort = new Dictionary<HierarchyInstancePortKey, int>();
        foreach (var resolved in resolvedInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var port in resolved.Ports.Where(
                port => port.Direction == PortDirection.Output))
            {
                var instancePort = new HierarchyInstancePortKey(
                    resolved.Occurrence,
                    resolved.Instance.Id,
                    port.Id);
                var terminal = new HierarchyTerminalKey(
                    resolved.Occurrence,
                    new InstanceTerminalReference(
                        resolved.Occurrence.Definition.Id,
                        resolved.Instance.Id,
                        port.Id));
                int? netOrdinal = runtimeNetByTerminal.TryGetValue(
                    terminal,
                    out var connectedNet)
                    ? connectedNet
                    : null;
                var ordinal = drivers.Count;
                drivers.Add(new SimulationDriver(
                    ordinal,
                    resolved.Ordinal,
                    netOrdinal,
                    port.Width));
                ordinalByInstancePort.Add(instancePort, ordinal);
                sources.Add(new SourceMapEntry(
                    ordinal,
                    Source(
                        resolved.Occurrence.Path,
                        new InstancePortSourceIdentity(
                            resolved.Occurrence.Definition.Id,
                            resolved.Instance.Id,
                            port.Id))));
            }
        }

        return (drivers.ToArray(), sources.ToArray(), ordinalByInstancePort);
    }

    private static (
        SimulationEvaluator[] Evaluators,
        SourceMapEntry[] Sources,
        EvaluatorInputSourceMapEntry[] InputSources) BuildHierarchyEvaluators(
            IReadOnlyList<HierarchyResolvedInstance> resolvedInstances,
            IReadOnlyDictionary<HierarchyTerminalKey, int> runtimeNetByTerminal,
            IReadOnlyDictionary<HierarchyInstancePortKey, int> driverByInstancePort,
            CancellationToken cancellationToken)
    {
        var evaluators = new SimulationEvaluator[resolvedInstances.Count];
        var sources = new SourceMapEntry[resolvedInstances.Count];
        var inputSources = new List<EvaluatorInputSourceMapEntry>();
        foreach (var resolved in resolvedInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPorts = resolved.Ports
                .Where(port => port.Direction == PortDirection.Input)
                .ToArray();
            var outputPorts = resolved.Ports
                .Where(port => port.Direction == PortDirection.Output)
                .ToArray();
            var inputNets = inputPorts.Select(port => runtimeNetByTerminal[
                new HierarchyTerminalKey(
                    resolved.Occurrence,
                    new InstanceTerminalReference(
                        resolved.Occurrence.Definition.Id,
                        resolved.Instance.Id,
                        port.Id))]).ToArray();
            var outputDrivers = outputPorts.Select(port => driverByInstancePort[
                new HierarchyInstancePortKey(
                    resolved.Occurrence,
                    resolved.Instance.Id,
                    port.Id)]).ToArray();
            evaluators[resolved.Ordinal] = new SimulationEvaluator(
                resolved.Ordinal,
                resolved.Kind,
                resolved.Width,
                inputNets,
                outputDrivers,
                GetInitialValue(resolved.Kind, resolved.Instance.Parameters),
                GetSlices(resolved.Kind, resolved.Instance.Parameters),
                GetOption(resolved.Kind, resolved.Instance.Parameters),
                GetClockSchedule(resolved.Kind, resolved.Instance.Parameters));
            sources[resolved.Ordinal] = new SourceMapEntry(
                resolved.Ordinal,
                Source(
                    resolved.Occurrence.Path,
                    new ComponentInstanceSourceIdentity(
                        resolved.Occurrence.Definition.Id,
                        resolved.Instance.Id)));
            for (var inputOrdinal = 0; inputOrdinal < inputPorts.Length; inputOrdinal++)
            {
                inputSources.Add(new EvaluatorInputSourceMapEntry(
                    resolved.Ordinal,
                    inputOrdinal,
                    Source(
                        resolved.Occurrence.Path,
                        new InstancePortSourceIdentity(
                            resolved.Occurrence.Definition.Id,
                            resolved.Instance.Id,
                            inputPorts[inputOrdinal].Id))));
            }
        }

        return (evaluators, sources, inputSources.ToArray());
    }

    private static (
        SimulationNet[] Nets,
        SourceMapEntry[] Sources,
        SourceMapEntry[] Aliases) BuildHierarchyNets(
            HierarchyTopology topology,
            IReadOnlyDictionary<HierarchyInstanceKey, HierarchyResolvedInstance>
                resolvedByOccurrenceAndId,
            IReadOnlyDictionary<HierarchyInstancePortKey, int> driverByInstancePort,
            CancellationToken cancellationToken)
    {
        var nets = new SimulationNet[topology.Groups.Length];
        var sources = new SourceMapEntry[topology.Groups.Length];
        var aliases = new List<SourceMapEntry>();
        for (var netOrdinal = 0; netOrdinal < topology.Groups.Length; netOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = topology.Groups[netOrdinal];
            var drivers = new SortedSet<int>();
            var receivers = new SortedSet<int>();
            foreach (var member in group.Members)
            {
                foreach (var terminal in member.Net.Terminals
                    .OfType<InstanceTerminalReference>())
                {
                    var instanceKey = new HierarchyInstanceKey(
                        member.Occurrence,
                        terminal.ComponentInstanceId);
                    if (!resolvedByOccurrenceAndId.TryGetValue(
                            instanceKey,
                            out var resolved))
                    {
                        continue;
                    }

                    var port = resolved.Ports.Single(candidate =>
                        string.Equals(candidate.Id, terminal.PortId, StringComparison.Ordinal));
                    var instancePort = new HierarchyInstancePortKey(
                        member.Occurrence,
                        terminal.ComponentInstanceId,
                        terminal.PortId);
                    if (port.Direction == PortDirection.Output)
                    {
                        drivers.Add(driverByInstancePort[instancePort]);
                    }
                    else
                    {
                        receivers.Add(resolved.Ordinal);
                    }
                }
            }

            nets[netOrdinal] = new SimulationNet(
                netOrdinal,
                group.Members[0].Net.Width,
                [.. drivers],
                [.. receivers]);
            var bindings = group.Members.Select(member => new SourceMapEntry(
                netOrdinal,
                Source(
                    member.Occurrence.Path,
                    new NetSourceIdentity(
                        member.Occurrence.Definition.Id,
                        member.Net.Id)))).ToArray();
            sources[netOrdinal] = bindings[0];
            aliases.AddRange(bindings.Skip(1));
        }

        return (nets, sources, aliases.ToArray());
    }

}
