using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

public static partial class Compiler
{
    private static CompilationOutcome CompileHierarchy(
        CompilationRequest request,
        CircuitDefinition entryDefinition,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<CompilerDiagnostic>();
        ValidateHierarchyTargets(
            request,
            entryDefinition,
            diagnostics,
            cancellationToken);
        if (diagnostics.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        var recursionWitness = FindRecursionWitness(
            request,
            entryDefinition,
            cancellationToken);
        if (recursionWitness is not null)
        {
            var locations = recursionWitness
                .Select(witness => CircuitLocation(witness.Path, new ComponentInstanceSourceIdentity(
                    witness.ContainingDefinitionId,
                    witness.InstanceId)))
                .Cast<CompilerSourceLocation>()
                .ToArray();
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_hierarchy_recursion",
                [
                    new CompilerDiagnosticArgument(
                        "cycleLength",
                        new CompilerUnsignedDecimalValue(
                            checked((ulong)recursionWitness.Length))),
                ],
                locations[0],
                locations));
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        var occurrenceResult = BuildOccurrences(
            request,
            entryDefinition,
            observations,
            cancellationToken);
        if (occurrenceResult.Rejection is not null)
        {
            return occurrenceResult.Rejection;
        }

        var occurrences = occurrenceResult.Occurrences;
        var resolvedInstances = ResolveHierarchyInstances(
            request,
            occurrences,
            diagnostics,
            cancellationToken);
        var topology = BuildHierarchyTopology(
            request,
            occurrences,
            occurrenceResult.ChildByCall,
            resolvedInstances,
            diagnostics,
            cancellationToken);
        if (diagnostics.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RejectInvalid(request, diagnostics, observations);
        }

        var driverCount = resolvedInstances.Aggregate(
            0UL,
            (count, instance) => checked(
                count
                + (ulong)instance.Ports.Count(
                    port => port.Direction == PortDirection.Output)));
        var elaboratedSlotCount = checked(
            (ulong)occurrences.Length
            + (ulong)resolvedInstances.Length
            + (ulong)topology.Groups.Length
            + driverCount);
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

        var artifact = BuildHierarchyArtifact(
            request,
            resolvedInstances,
            topology,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new CompilationSucceeded(
            artifact,
            [],
            CreateEvidence(request, observations, null));
    }

    private static void ValidateHierarchyTargets(
        CompilationRequest request,
        CircuitDefinition entryDefinition,
        List<CompilerDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<CircuitDefinitionId> { entryDefinition.Id };
        var pending = new Queue<(CircuitDefinition Definition, HierarchyPath Path)>();
        pending.Enqueue((
            entryDefinition,
            new HierarchyPath(request.EntryCircuitDefinitionId, [])));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (definition, path) = pending.Dequeue();
            foreach (var call in definition.ComponentInstances
                .Where(instance => instance.Target is CircuitDefinitionComponentTarget)
                .OrderBy(instance => instance.Id.Value, StringComparer.Ordinal))
            {
                var targetId = ((CircuitDefinitionComponentTarget)call.Target)
                    .CircuitDefinitionId;
                var target = request.ProjectRevision.Document.FindCircuitDefinition(targetId);
                if (target is null)
                {
                    diagnostics.Add(UnresolvedDefinitionTarget(
                        definition.Id,
                        path,
                        call,
                        targetId));
                    continue;
                }

                if (visited.Add(target.Id))
                {
                    pending.Enqueue((
                        target,
                        AppendPath(path, definition.Id, call.Id)));
                }
            }
        }
    }

    private static HierarchyCallWitness[]? FindRecursionWitness(
        CompilationRequest request,
        CircuitDefinition entryDefinition,
        CancellationToken cancellationToken)
    {
        var completed = new HashSet<CircuitDefinitionId>();
        var activeIndex = new Dictionary<CircuitDefinitionId, int>();
        var rootPath = new HierarchyPath(request.EntryCircuitDefinitionId, []);
        var stack = new List<HierarchyDfsFrame>
        {
            new(entryDefinition, rootPath, null),
        };
        activeIndex.Add(entryDefinition.Id, 0);

        while (stack.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = stack[^1];
            if (frame.NextCallIndex >= frame.Calls.Length)
            {
                activeIndex.Remove(frame.Definition.Id);
                completed.Add(frame.Definition.Id);
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            var call = frame.Calls[frame.NextCallIndex++];
            var targetId = ((CircuitDefinitionComponentTarget)call.Target)
                .CircuitDefinitionId;
            var target = request.ProjectRevision.Document.FindCircuitDefinition(targetId)!;

            var currentWitness = new HierarchyCallWitness(
                frame.Path,
                frame.Definition.Id,
                call.Id);
            if (activeIndex.TryGetValue(targetId, out var cycleStart))
            {
                var witness = new List<HierarchyCallWitness>();
                for (var index = cycleStart + 1; index < stack.Count; index++)
                {
                    witness.Add(stack[index].IncomingCall!);
                }

                witness.Add(currentWitness);
                return witness.ToArray();
            }

            if (completed.Contains(targetId))
            {
                continue;
            }

            var childPath = AppendPath(frame.Path, frame.Definition.Id, call.Id);
            var childFrame = new HierarchyDfsFrame(target, childPath, currentWitness);
            activeIndex.Add(targetId, stack.Count);
            stack.Add(childFrame);
        }

        return null;
    }

    private static CompilerDiagnostic UnresolvedDefinitionTarget(
        CircuitDefinitionId containingDefinitionId,
        HierarchyPath path,
        ComponentInstance call,
        CircuitDefinitionId targetId)
    {
        return new CompilerDiagnostic(
            "compiler_contract_unresolved",
            [
                new CompilerDiagnosticArgument(
                    "contractKey",
                    new CompilerContractKeyValue(new ComponentContractKey(
                        "logiclab.project",
                        targetId.Value))),
            ],
            CircuitLocation(
                path,
                new ComponentInstanceSourceIdentity(containingDefinitionId, call.Id)));
    }

    private static HierarchyOccurrenceResult BuildOccurrences(
        CompilationRequest request,
        CircuitDefinition entryDefinition,
        Dictionary<ProjectScaleDimension, ulong> observations,
        CancellationToken cancellationToken)
    {
        var occurrences = new List<HierarchyOccurrence>
        {
            new(entryDefinition, new HierarchyPath(request.EntryCircuitDefinitionId, [])),
        };
        var childByCall = new Dictionary<HierarchyCallKey, HierarchyOccurrence>();
        var hierarchyRejection = Observe(
            request,
            ProjectScaleDimension.HierarchyDepth,
            1,
            observations);
        if (hierarchyRejection is not null)
        {
            return new HierarchyOccurrenceResult([], childByCall, hierarchyRejection);
        }

        for (var index = 0; index < occurrences.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = occurrences[index];
            foreach (var call in occurrence.Definition.ComponentInstances
                .Where(instance => instance.Target is CircuitDefinitionComponentTarget)
                .OrderBy(instance => instance.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetId = ((CircuitDefinitionComponentTarget)call.Target)
                    .CircuitDefinitionId;
                var target = request.ProjectRevision.Document.FindCircuitDefinition(targetId)
                    ?? throw new InvalidOperationException(
                        "A validated Circuit Definition call target is missing.");
                var childPath = AppendPath(
                    occurrence.Path,
                    occurrence.Definition.Id,
                    call.Id);
                var depth = checked((ulong)childPath.Steps.Count + 1);
                var observedDepth = observations[ProjectScaleDimension.HierarchyDepth];
                if (depth > observedDepth)
                {
                    hierarchyRejection = Observe(
                        request,
                        ProjectScaleDimension.HierarchyDepth,
                        depth,
                        observations);
                    if (hierarchyRejection is not null)
                    {
                        return new HierarchyOccurrenceResult(
                            [],
                            childByCall,
                            hierarchyRejection);
                    }
                }

                var occurrenceCount = checked((ulong)occurrences.Count + 1);
                if (occurrenceCount > request.Policy.Maximum(
                        ProjectScaleDimension.ElaboratedSlotCount))
                {
                    var rejection = Observe(
                        request,
                        ProjectScaleDimension.ElaboratedSlotCount,
                        occurrenceCount,
                        observations);
                    return new HierarchyOccurrenceResult([], childByCall, rejection);
                }

                var child = new HierarchyOccurrence(target, childPath);
                occurrences.Add(child);
                childByCall.Add(new HierarchyCallKey(occurrence, call.Id), child);
            }
        }

        var canonicalOccurrences = occurrences
            .OrderBy(occurrence => PathKey(occurrence.Path), StringComparer.Ordinal)
            .ToArray();
        return new HierarchyOccurrenceResult(
            canonicalOccurrences,
            childByCall,
            null);
    }

    private static HierarchyResolvedInstance[] ResolveHierarchyInstances(
        CompilationRequest request,
        HierarchyOccurrence[] occurrences,
        List<CompilerDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var resolved = new List<HierarchyResolvedInstance>();
        foreach (var occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var instance in occurrence.Definition.ComponentInstances
                .Where(instance => instance.Target is LibraryComponentTarget)
                .OrderBy(instance => instance.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = ((LibraryComponentTarget)instance.Target).ContractKey;
                var schema = request.LibrarySnapshot.ResolveContract(key);
                if (schema is null)
                {
                    diagnostics.Add(new CompilerDiagnostic(
                        "compiler_contract_unresolved",
                        [
                            new CompilerDiagnosticArgument(
                                "contractKey",
                                new CompilerContractKeyValue(key)),
                        ],
                        CircuitLocation(
                            occurrence.Path,
                            new ComponentInstanceSourceIdentity(
                                occurrence.Definition.Id,
                                instance.Id))));
                    continue;
                }

                if (!TryGetEvaluatorKind(key, out var kind)
                    || !TryResolvePorts(instance, schema, out var ports))
                {
                    diagnostics.Add(new CompilerDiagnostic(
                        "compiler_parameter_schema_mismatch",
                        [
                            new CompilerDiagnosticArgument(
                                "contractKey",
                                new CompilerContractKeyValue(key)),
                            new CompilerDiagnosticArgument(
                                "parameterId",
                                new CompilerStableTokenValue("width")),
                            new CompilerDiagnosticArgument(
                                "rule",
                                new CompilerStableTokenValue("hierarchyCompilerContract")),
                        ],
                        CircuitLocation(
                            occurrence.Path,
                            new ComponentInstanceSourceIdentity(
                                occurrence.Definition.Id,
                                instance.Id))));
                    continue;
                }

                resolved.Add(new HierarchyResolvedInstance(
                    resolved.Count,
                    occurrence,
                    instance,
                    kind,
                    ports,
                    GetEvaluatorWidth(ports)));
            }
        }

        return resolved.ToArray();
    }

    private static HierarchyTopology BuildHierarchyTopology(
        CompilationRequest request,
        HierarchyOccurrence[] occurrences,
        Dictionary<HierarchyCallKey, HierarchyOccurrence> childByCall,
        HierarchyResolvedInstance[] resolvedInstances,
        List<CompilerDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var scopedNets = new List<HierarchyScopedNet>();
        var scopedNetByTerminal = new Dictionary<HierarchyTerminalKey, int>();
        foreach (var occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var net in occurrence.Definition.Nets
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scopedNetIndex = scopedNets.Count;
                scopedNets.Add(new HierarchyScopedNet(
                    scopedNetIndex,
                    occurrence,
                    net));
                foreach (var terminal in net.Terminals
                    .OrderBy(HierarchyTerminalSortKey, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (terminal.CircuitDefinitionId != occurrence.Definition.Id
                        || !TryResolveHierarchyPort(
                            request,
                            occurrence,
                            terminal,
                            out var port))
                    {
                        diagnostics.Add(PortUnresolved(
                            occurrence,
                            net,
                            terminal));
                        continue;
                    }

                    var terminalKey = new HierarchyTerminalKey(occurrence, terminal);
                    if (!scopedNetByTerminal.TryAdd(terminalKey, scopedNetIndex))
                    {
                        diagnostics.Add(PortUnresolved(
                            occurrence,
                            net,
                            terminal));
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
                            CircuitLocation(occurrence.Path, port.Source)));
                    }
                }
            }
        }

        ValidateRequiredHierarchyTerminals(
            request,
            occurrences,
            scopedNetByTerminal,
            diagnostics,
            cancellationToken);
        if (diagnostics.Count != 0)
        {
            return HierarchyTopology.Empty;
        }

        var union = new HierarchyNetUnion(scopedNets.Count);
        foreach (var occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var call in occurrence.Definition.ComponentInstances
                .Where(instance => instance.Target is CircuitDefinitionComponentTarget)
                .OrderBy(instance => instance.Id.Value, StringComparer.Ordinal))
            {
                var child = childByCall[new HierarchyCallKey(occurrence, call.Id)];
                foreach (var port in child.Definition.Ports)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var external = new HierarchyTerminalKey(
                        occurrence,
                        new InstanceTerminalReference(
                            occurrence.Definition.Id,
                            call.Id,
                            port.Id.Value));
                    var internalTerminal = new HierarchyTerminalKey(
                        child,
                        new DefinitionTerminalReference(
                            child.Definition.Id,
                            port.Id));
                    if (scopedNetByTerminal.TryGetValue(external, out var externalNet)
                        && scopedNetByTerminal.TryGetValue(
                            internalTerminal,
                            out var internalNet))
                    {
                        union.Union(externalNet, internalNet);
                    }
                }
            }
        }

        var groups = scopedNets
            .GroupBy(net => union.Find(net.Index))
            .Select(group => new HierarchyNetGroup(
                group.OrderBy(ScopedNetKey, StringComparer.Ordinal).ToArray()))
            .OrderBy(group => ScopedNetKey(group.Members[0]), StringComparer.Ordinal)
            .ToArray();
        var runtimeNetByScopedNet = new int[scopedNets.Count];
        for (var netOrdinal = 0; netOrdinal < groups.Length; netOrdinal++)
        {
            foreach (var member in groups[netOrdinal].Members)
            {
                runtimeNetByScopedNet[member.Index] = netOrdinal;
            }
        }

        return new HierarchyTopology(
            groups,
            scopedNetByTerminal,
            runtimeNetByScopedNet);
    }

    private static void ValidateRequiredHierarchyTerminals(
        CompilationRequest request,
        HierarchyOccurrence[] occurrences,
        Dictionary<HierarchyTerminalKey, int> scopedNetByTerminal,
        List<CompilerDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var port in occurrence.Definition.Ports.Where(
                port => port.Direction == PortDirection.Output))
            {
                RequireTerminal(
                    occurrence,
                    new DefinitionTerminalReference(occurrence.Definition.Id, port.Id),
                    new DefinitionPortSourceIdentity(occurrence.Definition.Id, port.Id),
                    scopedNetByTerminal,
                    diagnostics);
            }

            foreach (var instance in occurrence.Definition.ComponentInstances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (instance.Target)
                {
                    case LibraryComponentTarget library:
                        var schema = request.LibrarySnapshot.ResolveContract(
                            library.ContractKey);
                        if (schema is null)
                        {
                            continue;
                        }

                        if (!TryResolvePorts(instance, schema, out var ports))
                        {
                            continue;
                        }

                        foreach (var port in ports.Where(
                            port => port.Direction == PortDirection.Input))
                        {
                            RequireTerminal(
                                occurrence,
                                new InstanceTerminalReference(
                                    occurrence.Definition.Id,
                                    instance.Id,
                                    port.Id),
                                new InstancePortSourceIdentity(
                                    occurrence.Definition.Id,
                                    instance.Id,
                                    port.Id),
                                scopedNetByTerminal,
                                diagnostics);
                        }

                        break;
                    case CircuitDefinitionComponentTarget definitionTarget:
                        var target = request.ProjectRevision.Document.FindCircuitDefinition(
                            definitionTarget.CircuitDefinitionId);
                        if (target is null)
                        {
                            continue;
                        }

                        foreach (var port in target.Ports.Where(
                            port => port.Direction == PortDirection.Input))
                        {
                            RequireTerminal(
                                occurrence,
                                new InstanceTerminalReference(
                                    occurrence.Definition.Id,
                                    instance.Id,
                                    port.Id.Value),
                                new InstancePortSourceIdentity(
                                    occurrence.Definition.Id,
                                    instance.Id,
                                    port.Id.Value),
                                scopedNetByTerminal,
                                diagnostics);
                        }

                        break;
                    default:
                        throw new InvalidOperationException(
                            "The Component Target variant is undefined.");
                }
            }
        }
    }

    private static void RequireTerminal(
        HierarchyOccurrence occurrence,
        AuthoredTerminalReference terminal,
        AuthoredSourceIdentity source,
        Dictionary<HierarchyTerminalKey, int> scopedNetByTerminal,
        List<CompilerDiagnostic> diagnostics)
    {
        if (!scopedNetByTerminal.ContainsKey(new HierarchyTerminalKey(
                occurrence,
                terminal)))
        {
            diagnostics.Add(new CompilerDiagnostic(
                "compiler_required_terminal_unconnected",
                [],
                CircuitLocation(occurrence.Path, source)));
        }
    }

    private static bool TryResolveHierarchyPort(
        CompilationRequest request,
        HierarchyOccurrence occurrence,
        AuthoredTerminalReference terminal,
        out HierarchyPort port)
    {
        switch (terminal)
        {
            case DefinitionTerminalReference definitionTerminal:
                var definitionPort = occurrence.Definition.FindPort(
                    definitionTerminal.DefinitionPortId);
                if (definitionPort is null)
                {
                    port = default!;
                    return false;
                }

                port = new HierarchyPort(
                    definitionPort.Width,
                    new DefinitionPortSourceIdentity(
                        occurrence.Definition.Id,
                        definitionPort.Id));
                return true;
            case InstanceTerminalReference instanceTerminal:
                var instance = occurrence.Definition.FindComponentInstance(
                    instanceTerminal.ComponentInstanceId);
                if (instance is null)
                {
                    port = default!;
                    return false;
                }

                if (instance.Target is LibraryComponentTarget library)
                {
                    var schema = request.LibrarySnapshot.ResolveContract(library.ContractKey);
                    if (schema is null
                        || !TryResolvePorts(instance, schema, out var ports))
                    {
                        port = default!;
                        return false;
                    }

                    var componentPort = ports.SingleOrDefault(candidate =>
                        string.Equals(
                            candidate.Id,
                            instanceTerminal.PortId,
                            StringComparison.Ordinal));
                    if (componentPort is null)
                    {
                        port = default!;
                        return false;
                    }

                    port = new HierarchyPort(
                        componentPort.Width,
                        new InstancePortSourceIdentity(
                            occurrence.Definition.Id,
                            instance.Id,
                            componentPort.Id));
                    return true;
                }

                var definitionTarget = (CircuitDefinitionComponentTarget)instance.Target;
                var target = request.ProjectRevision.Document.FindCircuitDefinition(
                    definitionTarget.CircuitDefinitionId);
                var targetPort = target?.FindPort(instanceTerminal.PortId);
                if (targetPort is null)
                {
                    port = default!;
                    return false;
                }

                port = new HierarchyPort(
                    targetPort.Width,
                    new InstancePortSourceIdentity(
                        occurrence.Definition.Id,
                        instance.Id,
                        targetPort.Id.Value));
                return true;
            default:
                throw new InvalidOperationException(
                    "The Terminal Reference variant is undefined.");
        }
    }

    private static CompilerDiagnostic PortUnresolved(
        HierarchyOccurrence occurrence,
        Net net,
        AuthoredTerminalReference terminal)
    {
        var contractKey = terminal switch
        {
            InstanceTerminalReference instanceTerminal when
                occurrence.Definition.FindComponentInstance(
                    instanceTerminal.ComponentInstanceId)?.Target
                    is LibraryComponentTarget library => library.ContractKey,
            InstanceTerminalReference instanceTerminal when
                occurrence.Definition.FindComponentInstance(
                    instanceTerminal.ComponentInstanceId)?.Target
                    is CircuitDefinitionComponentTarget definition =>
                new ComponentContractKey(
                    "logiclab.project",
                    definition.CircuitDefinitionId.Value),
            _ => new ComponentContractKey(
                "logiclab.project",
                occurrence.Definition.Id.Value),
        };
        var portId = terminal switch
        {
            DefinitionTerminalReference definition => definition.DefinitionPortId.Value,
            InstanceTerminalReference instance => instance.PortId,
            _ => "invalid",
        };
        return new CompilerDiagnostic(
            "compiler_port_unresolved",
            [
                new CompilerDiagnosticArgument(
                    "contractKey",
                    new CompilerContractKeyValue(contractKey)),
                new CompilerDiagnosticArgument(
                    "portId",
                    new CompilerStableTokenValue(
                        StableToken.IsValid(portId) ? portId : "invalid")),
            ],
            CircuitLocation(
                occurrence.Path,
                new NetSourceIdentity(occurrence.Definition.Id, net.Id)));
    }

    private static HierarchyPath AppendPath(
        HierarchyPath path,
        CircuitDefinitionId containingDefinitionId,
        ComponentInstanceId instanceId)
    {
        return new HierarchyPath(
            path.EntryCircuitDefinitionId,
            path.Steps
                .Append(new HierarchyPathStep(containingDefinitionId, instanceId))
                .ToArray());
    }

    private static string PathKey(HierarchyPath path)
    {
        return string.Join(
            '\u0001',
            path.Steps.Select(step =>
                $"{step.ContainingCircuitDefinitionId.Value}\0{step.ComponentInstanceId.Value}"));
    }

    private static string ScopedNetKey(HierarchyScopedNet net)
    {
        return $"{PathKey(net.Occurrence.Path)}\u0002{net.Net.Id.Value}";
    }

    private static string HierarchyTerminalSortKey(AuthoredTerminalReference terminal)
    {
        return terminal switch
        {
            DefinitionTerminalReference definition =>
                $"0\0{definition.DefinitionPortId.Value}",
            InstanceTerminalReference instance =>
                $"1\0{instance.ComponentInstanceId.Value}\0{instance.PortId}",
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        };
    }

    private sealed class HierarchyDfsFrame
    {
        public HierarchyDfsFrame(
            CircuitDefinition definition,
            HierarchyPath path,
            HierarchyCallWitness? incomingCall)
        {
            Definition = definition;
            Path = path;
            IncomingCall = incomingCall;
            Calls = definition.ComponentInstances
                .Where(instance => instance.Target is CircuitDefinitionComponentTarget)
                .OrderBy(instance => instance.Id.Value, StringComparer.Ordinal)
                .ToArray();
        }

        public CircuitDefinition Definition { get; }

        public HierarchyPath Path { get; }

        public HierarchyCallWitness? IncomingCall { get; }

        public ComponentInstance[] Calls { get; }

        public int NextCallIndex { get; set; }
    }

    private sealed class HierarchyOccurrence
    {
        public HierarchyOccurrence(CircuitDefinition definition, HierarchyPath path)
        {
            Definition = definition;
            Path = path;
        }

        public CircuitDefinition Definition { get; }

        public HierarchyPath Path { get; }
    }

    private sealed class HierarchyNetUnion
    {
        private readonly int[] parents;

        public HierarchyNetUnion(int count)
        {
            parents = Enumerable.Range(0, count).ToArray();
        }

        public int Find(int value)
        {
            var root = value;
            while (parents[root] != root)
            {
                root = parents[root];
            }

            while (parents[value] != value)
            {
                var parent = parents[value];
                parents[value] = root;
                value = parent;
            }

            return root;
        }

        public void Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot == rightRoot)
            {
                return;
            }

            var retained = Math.Min(leftRoot, rightRoot);
            var removed = Math.Max(leftRoot, rightRoot);
            parents[removed] = retained;
        }
    }

    private sealed record HierarchyCallWitness(
        HierarchyPath Path,
        CircuitDefinitionId ContainingDefinitionId,
        ComponentInstanceId InstanceId);

    private sealed record HierarchyOccurrenceResult(
        HierarchyOccurrence[] Occurrences,
        Dictionary<HierarchyCallKey, HierarchyOccurrence> ChildByCall,
        CompilationRejected? Rejection);

    private sealed record HierarchyResolvedInstance(
        int Ordinal,
        HierarchyOccurrence Occurrence,
        ComponentInstance Instance,
        SimulationEvaluatorKind Kind,
        ResolvedComponentPortSchema[] Ports,
        uint Width);

    private sealed record HierarchyScopedNet(
        int Index,
        HierarchyOccurrence Occurrence,
        Net Net);

    private sealed record HierarchyNetGroup(HierarchyScopedNet[] Members);

    private sealed record HierarchyTopology(
        HierarchyNetGroup[] Groups,
        Dictionary<HierarchyTerminalKey, int> ScopedNetByTerminal,
        int[] RuntimeNetByScopedNet)
    {
        public static HierarchyTopology Empty { get; } = new(
            [],
            [],
            []);
    }

    private sealed record HierarchyPort(
        uint Width,
        AuthoredSourceIdentity Source);

    private readonly record struct HierarchyCallKey(
        HierarchyOccurrence Occurrence,
        ComponentInstanceId InstanceId);

    private readonly record struct HierarchyTerminalKey(
        HierarchyOccurrence Occurrence,
        AuthoredTerminalReference Terminal);

    private readonly record struct HierarchyInstanceKey(
        HierarchyOccurrence Occurrence,
        ComponentInstanceId InstanceId);

    private readonly record struct HierarchyInstancePortKey(
        HierarchyOccurrence Occurrence,
        ComponentInstanceId InstanceId,
        string PortId);
}
