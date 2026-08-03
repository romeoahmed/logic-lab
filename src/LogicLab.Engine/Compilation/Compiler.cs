using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

public static partial class Compiler
{
    public const string SemanticVersion = "logiclab.compiler.topology-v2";

    public static CompilationOutcome Compile(
        CompilationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observations = new Dictionary<ProjectScaleDimension, ulong>();
        try
        {
            return CompileCore(request, observations, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Reject(request, "compilation_cancelled", [], observations);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var diagnostic = new CompilerDiagnostic(
                "compiler_internal_invariant",
                [
                    new CompilerDiagnosticArgument(
                        "correlation",
                        new CompilerCorrelationTokenValue(
                            Guid.CreateVersion7().ToString("N"))),
                ],
                new CompilerProjectRootLocation(
                    request.ProjectRevision.Document.ProjectId));
            return Reject(
                request,
                "compilation_internal_defect",
                [diagnostic],
                observations);
        }
    }

    private static CompilationOutcome CompileCore(
        CompilationRequest request,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<CompilerDiagnostic>();
        ValidateLibrarySnapshot(request, diagnostics);
        var definition = request.ProjectRevision.Document.FindCircuitDefinition(
            request.EntryCircuitDefinitionId);
        if (definition is null)
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_entry_definition_missing",
                [],
                new CompilerProjectRootLocation(
                    request.ProjectRevision.Document.ProjectId)));

            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        if (diagnostics.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        var policyRejection = ObserveInitialDimensions(
            request,
            observations,
            cancellationToken);
        if (policyRejection is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return policyRejection;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (HasHierarchy(request.ProjectRevision.Document))
        {
            return CompileHierarchy(
                request,
                definition,
                observations,
                cancellationToken);
        }

        var hierarchyRejection = Observe(
            request,
            ProjectScaleDimension.HierarchyDepth,
            1,
            observations);
        if (hierarchyRejection is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return hierarchyRejection;
        }

        var pendingInstances = ResolveInstanceShapes(
            request,
            definition,
            diagnostics,
            cancellationToken);
        if (diagnostics.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        ulong portCount = 0;
        foreach (var instance in pendingInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            portCount = checked(portCount + instance.PortResolution.PortCount);
        }

        var elaboratedSlotCount = checked(
            (ulong)pendingInstances.Length
            + (ulong)definition.Nets.Count
            + portCount);
        var slotRejection = Observe(
            request,
            ProjectScaleDimension.ElaboratedSlotCount,
            elaboratedSlotCount,
            observations);
        if (slotRejection is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return slotRejection;
        }

        var memoryRejection = Observe(
            request,
            ProjectScaleDimension.MemoryCellCount,
            0,
            observations);
        if (memoryRejection is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return memoryRejection;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedInstances = MaterializeInstances(
            pendingInstances,
            cancellationToken);
        var netByTerminal = ValidateTopology(
            request,
            definition,
            resolvedInstances,
            diagnostics,
            cancellationToken);
        if (diagnostics.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        var artifact = BuildArtifact(
            request,
            definition,
            resolvedInstances,
            netByTerminal,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return new CompilationSucceeded(
            artifact,
            [],
            CreateEvidence(request, observations, null));
    }

    private static void ValidateLibrarySnapshot(
        CompilationRequest request,
        List<CompilerDiagnostic> diagnostics)
    {
        var expected = request.ProjectRevision.Document.LibrarySnapshot;
        var actual = request.LibrarySnapshot;
        var primary = new CompilerProjectRootLocation(
            request.ProjectRevision.Document.ProjectId);

        if (!string.Equals(
                expected.LibraryId,
                actual.LibraryId,
                StringComparison.Ordinal)
            || !string.Equals(
                expected.Version,
                actual.Version,
                StringComparison.Ordinal))
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_library_version_mismatch",
                [
                    new CompilerDiagnosticArgument(
                        "libraryId",
                        new CompilerStableTokenValue(expected.LibraryId)),
                    new CompilerDiagnosticArgument(
                        "expectedVersion",
                        new CompilerStableTokenValue(expected.Version)),
                    new CompilerDiagnosticArgument(
                        "actualVersion",
                        new CompilerStableTokenValue(actual.Version)),
                ],
                primary));
        }

        if (!string.Equals(
                expected.ContentDigest,
                actual.ContentDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_library_digest_mismatch",
                [
                    new CompilerDiagnosticArgument(
                        "libraryId",
                        new CompilerStableTokenValue(expected.LibraryId)),
                    new CompilerDiagnosticArgument(
                        "expected",
                        new CompilerDigestValue(expected.ContentDigest)),
                    new CompilerDiagnosticArgument(
                        "actual",
                        new CompilerDigestValue(actual.ContentDigest)),
                ],
                primary));
        }
    }

    private static CompilationRejected? ObserveInitialDimensions(
        CompilationRequest request,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        var document = request.ProjectRevision.Document;
        ulong entityCount = 0;
        foreach (var definition in document.CircuitDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entityCount = checked(
                entityCount
                + (ulong)definition.Ports.Count
                + (ulong)definition.ComponentInstances.Count
                + (ulong)definition.Nets.Count
                + (ulong)definition.Junctions.Count
                + (ulong)definition.WireGeometries.Count);
        }

        var dimensions = new[]
        {
            new ObservedProjectScaleDimension(
                ProjectScaleDimension.DefinitionCount,
                checked((ulong)document.CircuitDefinitions.Count)),
            new ObservedProjectScaleDimension(
                ProjectScaleDimension.EntityCount,
                entityCount),
        };

        foreach (var dimension in dimensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rejection = Observe(
                request,
                dimension.Dimension,
                dimension.Observed,
                observations);
            if (rejection is not null)
            {
                return rejection;
            }
        }

        return null;
    }

    private static CompilationRejected? Observe(
        CompilationRequest request,
        ProjectScaleDimension dimension,
        ulong observed,
        Dictionary<ProjectScaleDimension, ulong> observations)
    {
        observations[dimension] = observed;
        if (observed <= request.Policy.Maximum(dimension))
        {
            return null;
        }

        var breach = new ObservedProjectScaleDimension(dimension, observed);
        var diagnostic = new CompilerDiagnostic(
            "compiler_policy_exhausted",
            [
                new CompilerDiagnosticArgument(
                    "policyId",
                    new CompilerStableTokenValue(request.Policy.PolicyId)),
                new CompilerDiagnosticArgument(
                    "policyRevision",
                    new CompilerStableTokenValue(request.Policy.PolicyRevision)),
                new CompilerDiagnosticArgument(
                    "dimension",
                    new CompilerStableTokenValue(DimensionToken(dimension))),
                new CompilerDiagnosticArgument(
                    "observed",
                    new CompilerUnsignedDecimalValue(observed)),
            ],
            new CompilerProjectRootLocation(
                request.ProjectRevision.Document.ProjectId));
        return Reject(
            request,
            "compilation_policy_exhausted",
            [diagnostic],
            observations,
            breach);
    }

    private static PendingResolvedInstance[] ResolveInstanceShapes(
        CompilationRequest request,
        CircuitDefinition definition,
        List<CompilerDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var path = new HierarchyPath(request.EntryCircuitDefinitionId, []);
        var instances = definition.ComponentInstances
            .OrderBy(instance => instance.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var pending = new List<PendingResolvedInstance>(instances.Length);
        for (var ordinal = 0; ordinal < instances.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = instances[ordinal];
            var contractKey = ((LibraryComponentTarget)instance.Target).ContractKey;
            var schema = request.LibrarySnapshot.ResolveContract(contractKey);
            if (schema is null)
            {
                diagnostics.Add(new CompilerDiagnostic(
                    "compiler_contract_unresolved",
                    [
                        new CompilerDiagnosticArgument(
                            "contractKey",
                            new CompilerContractKeyValue(contractKey)),
                    ],
                    CircuitLocation(
                        path,
                        new ComponentInstanceSourceIdentity(definition.Id, instance.Id))));
                continue;
            }

            if (!TryGetEvaluatorKind(contractKey, out var kind)
                || !TryPreparePorts(
                    instance,
                    schema,
                    cancellationToken,
                    out var portResolution))
            {
                diagnostics.Add(new CompilerDiagnostic(
                    "compiler_parameter_schema_mismatch",
                    [
                        new CompilerDiagnosticArgument(
                            "contractKey",
                            new CompilerContractKeyValue(contractKey)),
                        new CompilerDiagnosticArgument(
                            "parameterId",
                            new CompilerStableTokenValue("width")),
                        new CompilerDiagnosticArgument(
                            "rule",
                            new CompilerStableTokenValue("flatCompilerContract")),
                    ],
                    CircuitLocation(
                        path,
                        new ComponentInstanceSourceIdentity(definition.Id, instance.Id))));
                continue;
            }

            pending.Add(new PendingResolvedInstance(
                ordinal,
                instance,
                contractKey,
                kind,
                portResolution));
        }

        return pending.ToArray();
    }

    private static ResolvedInstance[] MaterializeInstances(
        PendingResolvedInstance[] pendingInstances,
        CancellationToken cancellationToken)
    {
        var resolved = new ResolvedInstance[pendingInstances.Length];
        for (var index = 0; index < pendingInstances.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = pendingInstances[index];
            var ports = pending.PortResolution.Materialize(cancellationToken).ToArray();
            resolved[index] = new ResolvedInstance(
                pending.Ordinal,
                pending.Instance,
                pending.ContractKey,
                pending.Kind,
                ports,
                GetEvaluatorWidth(ports));
        }

        return resolved;
    }

    private static Dictionary<TerminalKey, int> ValidateTopology(
        CompilationRequest request,
        CircuitDefinition definition,
        ResolvedInstance[] resolvedInstances,
        List<CompilerDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var path = new HierarchyPath(request.EntryCircuitDefinitionId, []);
        var resolvedById = resolvedInstances.ToDictionary(item => item.Instance.Id);
        var orderedNets = definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var netByTerminal = new Dictionary<TerminalKey, int>();

        for (var netOrdinal = 0; netOrdinal < orderedNets.Length; netOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var net = orderedNets[netOrdinal];
            foreach (var terminal in net.Terminals
                .OfType<InstanceTerminalReference>()
                .OrderBy(
                item => item.ComponentInstanceId.Value,
                StringComparer.Ordinal).ThenBy(item => item.PortId, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (terminal.CircuitDefinitionId != definition.Id
                    || !resolvedById.TryGetValue(
                        terminal.ComponentInstanceId,
                        out var resolved))
                {
                    var authoredInstance = definition.FindComponentInstance(
                        terminal.ComponentInstanceId);
                    diagnostics.Add(new CompilerDiagnostic(
                        "compiler_port_unresolved",
                        [
                            new CompilerDiagnosticArgument(
                                "contractKey",
                                new CompilerContractKeyValue(
                                    authoredInstance?.Target is LibraryComponentTarget library
                                        ? library.ContractKey
                                        : new ComponentContractKey(
                                            CoreLibrarySchema.LibraryId,
                                            "unresolved"))),
                            new CompilerDiagnosticArgument(
                                "portId",
                                new CompilerStableTokenValue(
                                    StableToken.IsValid(terminal.PortId)
                                        ? terminal.PortId
                                        : "invalid")),
                        ],
                        CircuitLocation(
                            path,
                            new NetSourceIdentity(definition.Id, net.Id))));
                    continue;
                }

                var port = resolved.Ports.SingleOrDefault(candidate =>
                    string.Equals(candidate.Id, terminal.PortId, StringComparison.Ordinal));
                if (port is null)
                {
                    diagnostics.Add(new CompilerDiagnostic(
                        "compiler_port_unresolved",
                        [
                            new CompilerDiagnosticArgument(
                                "contractKey",
                                new CompilerContractKeyValue(resolved.ContractKey)),
                            new CompilerDiagnosticArgument(
                                "portId",
                                new CompilerStableTokenValue(
                                    StableToken.IsValid(terminal.PortId)
                                        ? terminal.PortId
                                        : "invalid")),
                        ],
                        CircuitLocation(
                            path,
                            new InstancePortSourceIdentity(
                                definition.Id,
                                terminal.ComponentInstanceId,
                                terminal.PortId))));
                    continue;
                }

                if (port.Width != net.Width)
                {
                    diagnostics.Add(new CompilerDiagnostic(
                        "compiler_width_mismatch",
                        [
                            new CompilerDiagnosticArgument(
                                "expected",
                                new CompilerUnsignedDecimalValue(port.Width)),
                            new CompilerDiagnosticArgument(
                                "actual",
                                new CompilerUnsignedDecimalValue(net.Width)),
                        ],
                        CircuitLocation(
                            path,
                            new InstancePortSourceIdentity(
                                definition.Id,
                                terminal.ComponentInstanceId,
                                terminal.PortId))));
                }

                netByTerminal.TryAdd(
                    new TerminalKey(terminal.ComponentInstanceId, terminal.PortId),
                    netOrdinal);
            }
        }

        foreach (var resolved in resolvedInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var port in resolved.Ports.Where(
                item => item.Direction == PortDirection.Input))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var terminal = new TerminalKey(resolved.Instance.Id, port.Id);
                if (!netByTerminal.ContainsKey(terminal))
                {
                    diagnostics.Add(new CompilerDiagnostic(
                        "compiler_required_terminal_unconnected",
                        [],
                        CircuitLocation(
                            path,
                            new InstancePortSourceIdentity(
                                definition.Id,
                                resolved.Instance.Id,
                                port.Id))));
                }
            }
        }

        return netByTerminal;
    }

    private static CompilationArtifact BuildArtifact(
        CompilationRequest request,
        CircuitDefinition definition,
        ResolvedInstance[] resolvedInstances,
        Dictionary<TerminalKey, int> netByTerminal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = new HierarchyPath(request.EntryCircuitDefinitionId, []);
        var orderedNets = definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var evaluatorOrdinalById = resolvedInstances.ToDictionary(
            item => item.Instance.Id,
            item => item.Ordinal);
        var inputTerminals = resolvedInstances
            .SelectMany(resolved => resolved.Ports
                .Where(port => port.Direction == PortDirection.Input)
                .Select(port => new TerminalKey(resolved.Instance.Id, port.Id)))
            .ToHashSet();

        var (drivers, driverSources, driverByTerminal) = BuildDrivers(
            path,
            definition,
            resolvedInstances,
            netByTerminal,
            cancellationToken);
        var (evaluators, evaluatorSources, evaluatorInputSources) = BuildEvaluators(
            path,
            definition,
            resolvedInstances,
            netByTerminal,
            driverByTerminal,
            cancellationToken);
        var (simulationNets, netSources) = BuildNets(
            path,
            definition,
            orderedNets,
            evaluatorOrdinalById,
            inputTerminals,
            driverByTerminal,
            cancellationToken);
        var (fanoutOffsets, fanoutEvaluators) = BuildFanout(
            simulationNets,
            cancellationToken);
        var adjacency = BuildEvaluatorAdjacency(
            evaluators.Length,
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
                evaluatorOrdinal =>
                    new StronglyConnectedComponentMemberSourceMapEntry(
                        component.Ordinal,
                        evaluatorOrdinal,
                        evaluatorSources[evaluatorOrdinal].Source)))
            .ToArray();
        var sourceMap = new SourceMap(
            evaluatorSources,
            evaluatorInputSources,
            driverSources,
            netSources,
            sccMemberSources);
        CompilationArtifactValidator.Validate(
            simulationIr,
            sourceMap,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CompilationArtifactKey(
            request.ProjectRevision.RevisionId,
            request.EntryCircuitDefinitionId,
            request.LibrarySnapshot.Fingerprint,
            SemanticVersion);
        return new CompilationArtifact(
            key,
            simulationIr,
            sourceMap,
            request.ProjectRevision);
    }

    private static (
        SimulationDriver[] Drivers,
        SourceMapEntry[] Sources,
        Dictionary<TerminalKey, int> OrdinalByTerminal) BuildDrivers(
        HierarchyPath path,
        CircuitDefinition definition,
        ResolvedInstance[] resolvedInstances,
        Dictionary<TerminalKey, int> netByTerminal,
        CancellationToken cancellationToken)
    {
        var drivers = new List<SimulationDriver>();
        var driverSources = new List<SourceMapEntry>();
        var driverByTerminal = new Dictionary<TerminalKey, int>();
        foreach (var resolved in resolvedInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var port in resolved.Ports.Where(
                item => item.Direction == PortDirection.Output))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var terminal = new TerminalKey(resolved.Instance.Id, port.Id);
                var driverOrdinal = drivers.Count;
                int? netOrdinal = netByTerminal.TryGetValue(
                    terminal,
                    out var connectedNetOrdinal)
                    ? connectedNetOrdinal
                    : null;
                drivers.Add(new SimulationDriver(
                    driverOrdinal,
                    resolved.Ordinal,
                    netOrdinal,
                    port.Width));
                driverByTerminal.Add(terminal, driverOrdinal);
                driverSources.Add(new SourceMapEntry(
                    driverOrdinal,
                    Source(
                        path,
                        new InstancePortSourceIdentity(
                            definition.Id,
                            resolved.Instance.Id,
                            port.Id))));
            }
        }

        return (drivers.ToArray(), driverSources.ToArray(), driverByTerminal);
    }

    private static (
        SimulationEvaluator[] Evaluators,
        SourceMapEntry[] Sources,
        EvaluatorInputSourceMapEntry[] InputSources) BuildEvaluators(
        HierarchyPath path,
        CircuitDefinition definition,
        ResolvedInstance[] resolvedInstances,
        Dictionary<TerminalKey, int> netByTerminal,
        Dictionary<TerminalKey, int> driverByTerminal,
        CancellationToken cancellationToken)
    {
        var evaluators = new SimulationEvaluator[resolvedInstances.Length];
        var evaluatorSources = new SourceMapEntry[resolvedInstances.Length];
        var evaluatorInputSources = new List<EvaluatorInputSourceMapEntry>();
        foreach (var resolved in resolvedInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPorts = resolved.Ports
                .Where(port => port.Direction == PortDirection.Input)
                .ToArray();
            var outputPorts = resolved.Ports
                .Where(port => port.Direction == PortDirection.Output)
                .ToArray();
            var inputNets = inputPorts
                .Select(port => netByTerminal[
                    new TerminalKey(resolved.Instance.Id, port.Id)])
                .ToArray();
            var outputDrivers = outputPorts
                .Select(port => driverByTerminal[
                    new TerminalKey(resolved.Instance.Id, port.Id)])
                .ToArray();
            evaluators[resolved.Ordinal] = new SimulationEvaluator(
                resolved.Ordinal,
                resolved.Kind,
                resolved.Width,
                inputNets,
                outputDrivers,
                GetInitialValue(resolved),
                GetSlices(resolved));
            evaluatorSources[resolved.Ordinal] = new SourceMapEntry(
                resolved.Ordinal,
                Source(
                    path,
                    new ComponentInstanceSourceIdentity(
                        definition.Id,
                        resolved.Instance.Id)));
            for (var inputOrdinal = 0; inputOrdinal < inputPorts.Length; inputOrdinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                evaluatorInputSources.Add(new EvaluatorInputSourceMapEntry(
                    resolved.Ordinal,
                    inputOrdinal,
                    Source(
                        path,
                        new InstancePortSourceIdentity(
                            definition.Id,
                            resolved.Instance.Id,
                            inputPorts[inputOrdinal].Id))));
            }
        }

        return (evaluators, evaluatorSources, evaluatorInputSources.ToArray());
    }

    private static (
        SimulationNet[] Nets,
        SourceMapEntry[] Sources) BuildNets(
        HierarchyPath path,
        CircuitDefinition definition,
        Net[] orderedNets,
        Dictionary<ComponentInstanceId, int> evaluatorOrdinalById,
        HashSet<TerminalKey> inputTerminals,
        Dictionary<TerminalKey, int> driverByTerminal,
        CancellationToken cancellationToken)
    {
        var simulationNets = new SimulationNet[orderedNets.Length];
        var netSources = new SourceMapEntry[orderedNets.Length];
        for (var netOrdinal = 0; netOrdinal < orderedNets.Length; netOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var net = orderedNets[netOrdinal];
            var netDrivers = net.Terminals
                .OfType<InstanceTerminalReference>()
                .Select(terminal => new TerminalKey(
                    terminal.ComponentInstanceId,
                    terminal.PortId))
                .Where(driverByTerminal.ContainsKey)
                .Select(terminal => driverByTerminal[terminal])
                .Order()
                .ToArray();
            var receivers = net.Terminals
                .OfType<InstanceTerminalReference>()
                .Where(terminal => inputTerminals.Contains(new TerminalKey(
                    terminal.ComponentInstanceId,
                    terminal.PortId)))
                .Select(terminal => evaluatorOrdinalById[terminal.ComponentInstanceId])
                .Order()
                .ToArray();
            simulationNets[netOrdinal] = new SimulationNet(
                netOrdinal,
                net.Width,
                netDrivers,
                receivers);
            netSources[netOrdinal] = new SourceMapEntry(
                netOrdinal,
                Source(path, new NetSourceIdentity(definition.Id, net.Id)));
        }

        return (simulationNets, netSources);
    }

    private static (
        int[] Offsets,
        int[] EvaluatorOrdinals) BuildFanout(
        SimulationNet[] simulationNets,
        CancellationToken cancellationToken)
    {
        var fanoutOffsets = new int[simulationNets.Length + 1];
        var fanoutEvaluators = new List<int>();
        for (var netOrdinal = 0; netOrdinal < simulationNets.Length; netOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fanoutOffsets[netOrdinal] = fanoutEvaluators.Count;
            fanoutEvaluators.AddRange(simulationNets[netOrdinal].ReceiverEvaluatorOrdinals);
        }

        fanoutOffsets[^1] = fanoutEvaluators.Count;
        return (fanoutOffsets, fanoutEvaluators.ToArray());
    }

    private static int[][] BuildEvaluatorAdjacency(
        int evaluatorCount,
        SimulationDriver[] drivers,
        SimulationNet[] simulationNets,
        CancellationToken cancellationToken)
    {
        var adjacency = Enumerable.Range(0, evaluatorCount)
            .Select(_ => new SortedSet<int>())
            .ToArray();
        foreach (var driver in drivers.Where(item => item.NetOrdinal is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var receiver in simulationNets[driver.NetOrdinal!.Value]
                .ReceiverEvaluatorOrdinals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                adjacency[driver.EvaluatorOrdinal].Add(receiver);
            }
        }

        return adjacency.Select(edges => edges.ToArray()).ToArray();
    }

    private static LogicVector? GetInitialValue(ResolvedInstance instance)
    {
        if (instance.Kind is not (SimulationEvaluatorKind.InputSource
            or SimulationEvaluatorKind.ConstantSource))
        {
            return null;
        }

        var values = instance.Instance.Parameters
            .Single(binding => string.Equals(
                binding.ParameterId,
                instance.Kind == SimulationEvaluatorKind.InputSource
                    ? "initialValue"
                    : "value",
                StringComparison.Ordinal))
            .Value as LogicVectorParameterValue;
        return new LogicVector(values!.Values);
    }

    private static bool TryGetEvaluatorKind(
        ComponentContractKey key,
        out SimulationEvaluatorKind kind)
    {
        switch (key.ContractId)
        {
            case "source.input":
                kind = SimulationEvaluatorKind.InputSource;
                return true;
            case "source.constant":
                kind = SimulationEvaluatorKind.ConstantSource;
                return true;
            case "logic.not":
                kind = SimulationEvaluatorKind.LogicNot;
                return true;
            case "sink.output":
                kind = SimulationEvaluatorKind.OutputSink;
                return true;
            case "topology.split":
                kind = SimulationEvaluatorKind.TopologySplit;
                return true;
            case "topology.concat":
                kind = SimulationEvaluatorKind.TopologyConcat;
                return true;
            case "topology.zero_extend":
                kind = SimulationEvaluatorKind.TopologyZeroExtend;
                return true;
            case "topology.sign_extend":
                kind = SimulationEvaluatorKind.TopologySignExtend;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryPreparePorts(
        ComponentInstance instance,
        ComponentContractSchema schema,
        CancellationToken cancellationToken,
        out ComponentPortResolution resolution)
    {
        try
        {
            resolution = schema.PreparePorts(
                instance.Parameters,
                cancellationToken);
            return resolution.PortCount > 0;
        }
        catch (ArgumentException)
        {
            resolution = null!;
            return false;
        }
    }

    private static uint GetEvaluatorWidth(
        ResolvedComponentPortSchema[] ports)
    {
        return ports.SingleOrDefault(port => string.Equals(
                port.Id,
                "Q",
                StringComparison.Ordinal))?.Width
            ?? ports[0].Width;
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<BitSlice> GetSlices(
        ResolvedInstance instance)
    {
        return instance.Kind == SimulationEvaluatorKind.TopologySplit
            ? ((SlicesParameterValue)instance.Instance.Parameters.Single(binding =>
                string.Equals(
                    binding.ParameterId,
                    "slices",
                    StringComparison.Ordinal)).Value).Values
            : Array.AsReadOnly<BitSlice>([]);
    }

    private static CompilationSource Source(
        HierarchyPath path,
        AuthoredSourceIdentity identity)
    {
        return new CompilationSource(identity, path);
    }

    private static CompilerCircuitLocation CircuitLocation(
        HierarchyPath path,
        AuthoredSourceIdentity identity)
    {
        return new CompilerCircuitLocation(Source(path, identity));
    }

    private static CompilationRejected Reject(
        CompilationRequest request,
        string reason,
        CompilerDiagnostic[] diagnostics,
        Dictionary<ProjectScaleDimension, ulong> observations,
        ObservedProjectScaleDimension? breach = null)
    {
        return new CompilationRejected(
            reason,
            diagnostics,
            CreateEvidence(request, observations, breach));
    }

    private static CompilationRejected RejectInvalid(
        CompilationRequest request,
        IEnumerable<CompilerDiagnostic> diagnostics,
        Dictionary<ProjectScaleDimension, ulong> observations)
    {
        return Reject(
            request,
            "compilation_invalid",
            CompilerCanonicalizer.Diagnostics(diagnostics),
            observations);
    }

    private static CompilationEvidence CreateEvidence(
        CompilationRequest request,
        Dictionary<ProjectScaleDimension, ulong> observations,
        ObservedProjectScaleDimension? breach)
    {
        return new CompilationEvidence(
            request.ProjectRevision.RevisionId,
            request.EntryCircuitDefinitionId,
            request.LibrarySnapshot.Fingerprint,
            SemanticVersion,
            new CompilationPolicyReference(
                request.Policy.PolicyId,
                request.Policy.PolicyRevision),
            observations
                .OrderBy(
                    row => DimensionToken(row.Key),
                    StringComparer.Ordinal)
                .Select(row => new ObservedProjectScaleDimension(row.Key, row.Value))
                .ToArray(),
            breach);
    }

    private static string DimensionToken(ProjectScaleDimension dimension)
    {
        return dimension switch
        {
            ProjectScaleDimension.DefinitionCount => "definition_count",
            ProjectScaleDimension.EntityCount => "entity_count",
            ProjectScaleDimension.HierarchyDepth => "hierarchy_depth",
            ProjectScaleDimension.ElaboratedSlotCount => "elaborated_slot_count",
            ProjectScaleDimension.MemoryCellCount => "memory_cell_count",
            _ => throw new InvalidOperationException(
                "The Project Scale Dimension variant is undefined."),
        };
    }

    private static bool HasHierarchy(ProjectDocument document)
    {
        return document.CircuitDefinitions.Any(definition =>
            definition.Ports.Count != 0
            || definition.ComponentInstances.Any(instance =>
                instance.Target is CircuitDefinitionComponentTarget));
    }

    private readonly record struct TerminalKey(
        ComponentInstanceId ComponentInstanceId,
        string PortId);

    private sealed record ResolvedInstance(
        int Ordinal,
        ComponentInstance Instance,
        ComponentContractKey ContractKey,
        SimulationEvaluatorKind Kind,
        ResolvedComponentPortSchema[] Ports,
        uint Width);

    private sealed record PendingResolvedInstance(
        int Ordinal,
        ComponentInstance Instance,
        ComponentContractKey ContractKey,
        SimulationEvaluatorKind Kind,
        ComponentPortResolution PortResolution);
}
