using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    private static EditOutcome ApplyRenameDefinition(
        ProjectRevision revision,
        RenameCircuitDefinitionIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateDisplayText(intent.DisplayName, "displayName", diagnostics);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        return Commit(
            revision,
            definition.WithDisplayName(intent.DisplayName),
            [new CircuitRootSourceIdentity(definition.Id)]);
    }

    private static EditOutcome ApplyMoveDefinitionPorts(
        ProjectRevision revision,
        MoveDefinitionPortsIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        if (intent.Moves.Count == 0)
        {
            diagnostics.Add(MissingReference("definitionPort"));
        }

        var moves = new Dictionary<DefinitionPortId, DefinitionPortPlacement>();
        foreach (var move in intent.Moves)
        {
            if (move.DefinitionPortId is null
                || !moves.TryAdd(move.DefinitionPortId, move.Placement))
            {
                diagnostics.Add(DuplicateId("definitionPort"));
                continue;
            }

            if (definition.FindPort(move.DefinitionPortId) is null)
            {
                diagnostics.Add(MissingReference("definitionPort"));
            }

            if (!Enum.IsDefined(move.Placement.Facing))
            {
                diagnostics.Add(InvalidCoordinate("definitionPortPlacement", "facing"));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var ports = definition.Ports.Select(port =>
        {
            var placement = moves.GetValueOrDefault(port.Id, port.Placement);
            return new DefinitionPort(
                port.Id,
                port.DisplayName,
                port.Direction,
                port.Width,
                placement);
        }).ToArray();
        return Commit(
            revision,
            definition.WithPorts(ports),
            moves.Keys.Select(id => (AuthoredSourceIdentity)
                new DefinitionPortSourceIdentity(definition.Id, id)).ToArray());
    }

    private static EditOutcome ApplyRemoveDefinition(
        ProjectRevision revision,
        RemoveCircuitDefinitionIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        if (revision.Document.EntryCircuitDefinitionId == intent.CircuitDefinitionId)
        {
            return Reject(DeleteHasDependents("entryCircuitDefinition", 1));
        }

        var dependentCount = revision.Document.CircuitDefinitions
            .SelectMany(candidate => candidate.ComponentInstances)
            .Count(instance => instance.Target is CircuitDefinitionComponentTarget target
                && target.CircuitDefinitionId == intent.CircuitDefinitionId);
        if (dependentCount != 0)
        {
            return Reject(DeleteHasDependents("componentInstance", dependentCount));
        }

        var removed = DefinitionSources(definition).ToArray();
        return Commit(
            revision,
            revision.Document.RemoveCircuitDefinition(intent.CircuitDefinitionId),
            [],
            removed);
    }

    private static EditOutcome ApplyRenameInstance(
        ProjectRevision revision,
        RenameComponentInstanceIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        var instance = definition?.FindComponentInstance(intent.ComponentInstanceId);
        if (definition is null || instance is null)
        {
            return Reject(MissingReference(
                definition is null ? "circuitDefinition" : "componentInstance"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        if (intent.DisplayName is not null)
        {
            ValidateDisplayText(intent.DisplayName, "displayName", diagnostics);
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var updated = definition.ReplaceComponentInstances(
            [instance.WithDisplayName(intent.DisplayName)]);
        return Commit(
            revision,
            updated,
            [new ComponentInstanceSourceIdentity(definition.Id, instance.Id)]);
    }

    private static EditOutcome ApplySetInstanceParameters(
        ProjectRevision revision,
        SetInstanceParametersIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        var instance = definition?.FindComponentInstance(intent.ComponentInstanceId);
        if (definition is null || instance is null)
        {
            return Reject(MissingReference(
                definition is null ? "circuitDefinition" : "componentInstance"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        var oldPorts = ResolveTargetPorts(
            revision.Document,
            instance.Target,
            instance.Parameters,
            diagnostics);
        var newPorts = ResolveTargetPorts(
            revision.Document,
            instance.Target,
            intent.Parameters,
            diagnostics);
        if (diagnostics.Count == 0 && !PortSchemasMatch(oldPorts, newPorts))
        {
            diagnostics.Add(InvalidParameter(instance.Target, "portSchemaChanged"));
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var replacement = instance.WithParameters(intent.Parameters.ToArray());
        return Commit(
            revision,
            definition.ReplaceComponentInstances([replacement]),
            [new ComponentInstanceSourceIdentity(definition.Id, instance.Id)]);
    }

    private static EditOutcome ApplyRemoveInstances(
        ProjectRevision revision,
        RemoveComponentInstancesIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        if (intent.ComponentInstanceIds.Count == 0)
        {
            diagnostics.Add(MissingReference("componentInstance"));
        }

        var ids = new HashSet<ComponentInstanceId>();
        foreach (var id in intent.ComponentInstanceIds)
        {
            if (!ids.Add(id))
            {
                diagnostics.Add(DuplicateId("componentInstance"));
            }
            else if (definition.FindComponentInstance(id) is null)
            {
                diagnostics.Add(MissingReference("componentInstance"));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var removedNetIds = new HashSet<NetId>();
        var nets = definition.Nets.Select(net =>
        {
            var terminals = net.Terminals
                .Where(terminal => terminal is not InstanceTerminalReference instance
                    || !ids.Contains(instance.ComponentInstanceId))
                .ToArray();
            var hasGeometry = definition.WireGeometries.Any(geometry =>
                geometry.NetId == net.Id);
            if (terminals.Length == 0 && net.JunctionIds.Count == 0 && !hasGeometry)
            {
                removedNetIds.Add(net.Id);
            }

            return net.WithMembership(terminals, net.JunctionIds.ToArray());
        }).Where(net => !removedNetIds.Contains(net.Id)).ToArray();
        var instances = definition.ComponentInstances
            .Where(instance => !ids.Contains(instance.Id))
            .ToArray();
        var updated = definition.WithComponentsAndTopology(instances, nets);
        var removed = ids.Select(id => (AuthoredSourceIdentity)
                new ComponentInstanceSourceIdentity(definition.Id, id))
            .Concat(removedNetIds.Select(id => (AuthoredSourceIdentity)
                new NetSourceIdentity(definition.Id, id)))
            .ToArray();
        var changed = nets
            .Where(net => definition.FindNet(net.Id)!.Terminals.Count != net.Terminals.Count)
            .Select(net => (AuthoredSourceIdentity)new NetSourceIdentity(definition.Id, net.Id))
            .ToArray();
        return Commit(revision, updated, changed, removed);
    }

    private static EditOutcome ApplyChangeInstanceContract(
        ProjectRevision revision,
        ChangeInstanceContractIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        var instance = definition?.FindComponentInstance(intent.ComponentInstanceId);
        if (definition is null || instance is null)
        {
            return Reject(MissingReference(
                definition is null ? "circuitDefinition" : "componentInstance"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        var oldPorts = ResolveTargetPorts(
            revision.Document,
            instance.Target,
            instance.Parameters,
            diagnostics);
        var newPorts = ResolveTargetPorts(
            revision.Document,
            intent.Target,
            intent.Parameters,
            diagnostics);
        var migration = ValidateInstancePortMigration(
            oldPorts,
            newPorts,
            intent.Ports,
            diagnostics);
        ValidateSymbolVariant(
            revision.Document.SymbolProfile,
            intent.Target,
            intent.Parameters,
            intent.SymbolVariantId,
            diagnostics);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var topology = MigrateInstanceTerminals(
            definition,
            instance.Id,
            migration!);
        var replacement = instance.WithContract(
            intent.Target,
            intent.Parameters.ToArray(),
            intent.SymbolVariantId);
        var updated = definition.WithComponentsAndTopology(
            definition.ComponentInstances.Select(candidate =>
                candidate.Id == instance.Id ? replacement : candidate).ToArray(),
            topology.Nets);
        var changed = topology.ChangedNetIds.Select(id => (AuthoredSourceIdentity)
                new NetSourceIdentity(definition.Id, id))
            .Append(new ComponentInstanceSourceIdentity(definition.Id, instance.Id))
            .ToArray();
        var removed = topology.RemovedNetIds.Select(id => (AuthoredSourceIdentity)
            new NetSourceIdentity(definition.Id, id)).ToArray();
        return Commit(revision, updated, changed, removed);
    }

    private static EditOutcome ApplyChangePublicPortContract(
        ProjectRevision revision,
        ChangePublicPortContractIntent intent)
    {
        var target = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (target is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        var newPorts = BuildPublicPortContract(target, intent.Ports, diagnostics);
        var callSites = revision.Document.CircuitDefinitions
            .SelectMany(definition => definition.ComponentInstances
                .Where(instance => instance.Target is CircuitDefinitionComponentTarget t
                    && t.CircuitDefinitionId == target.Id)
                .Select(instance => (Definition: definition, Instance: instance)))
            .OrderBy(item => item.Definition.Id.Value, StringComparer.Ordinal)
            .ThenBy(item => item.Instance.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var migrationByCallSite = ValidateCallSiteMigrations(
            target,
            newPorts,
            callSites,
            intent.CallSites,
            diagnostics);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var newPortIds = newPorts.Select(port => port.Id).ToHashSet();
        var targetTopology = RemoveObsoleteDefinitionTerminals(target, newPortIds);
        var updatedTarget = target.WithPorts(newPorts);
        updatedTarget = updatedTarget.WithComponentsAndTopology(
            updatedTarget.ComponentInstances.ToArray(),
            targetTopology.Nets);
        var replacements = new Dictionary<CircuitDefinitionId, CircuitDefinition>
        {
            [target.Id] = updatedTarget,
        };
        var changedTopology = targetTopology.ChangedNetIds.Select(id =>
            (AuthoredSourceIdentity)new NetSourceIdentity(target.Id, id)).ToList();
        var removedTopology = targetTopology.RemovedNetIds.Select(id =>
            (AuthoredSourceIdentity)new NetSourceIdentity(target.Id, id)).ToList();
        foreach (var callSite in callSites)
        {
            var current = replacements.GetValueOrDefault(
                callSite.Definition.Id,
                callSite.Definition);
            var migration = migrationByCallSite![
                (callSite.Definition.Id, callSite.Instance.Id)];
            var topology = MigrateInstanceTerminals(
                current,
                callSite.Instance.Id,
                migration);
            replacements[current.Id] = current.WithComponentsAndTopology(
                current.ComponentInstances.ToArray(),
                topology.Nets);
            changedTopology.AddRange(topology.ChangedNetIds.Select(id =>
                (AuthoredSourceIdentity)new NetSourceIdentity(current.Id, id)));
            removedTopology.AddRange(topology.RemovedNetIds.Select(id =>
                (AuthoredSourceIdentity)new NetSourceIdentity(current.Id, id)));
        }

        var oldIds = target.Ports.Select(port => port.Id).ToHashSet();
        var removed = oldIds.Except(newPortIds).Select(id => (AuthoredSourceIdentity)
                new DefinitionPortSourceIdentity(target.Id, id))
            .Concat(removedTopology)
            .ToArray();
        var changed = newPorts.Select(port => (AuthoredSourceIdentity)
                new DefinitionPortSourceIdentity(target.Id, port.Id))
            .Prepend(new CircuitRootSourceIdentity(target.Id))
            .Concat(callSites.Select(item => (AuthoredSourceIdentity)
                new ComponentInstanceSourceIdentity(item.Definition.Id, item.Instance.Id)))
            .Concat(changedTopology)
            .ToArray();
        return Commit(
            revision,
            revision.Document.ReplaceCircuitDefinitions(replacements.Values.ToArray()),
            changed,
            removed);
    }

    private static DefinitionPort[] BuildPublicPortContract(
        CircuitDefinition target,
        ReadOnlyCollection<DefinitionPortContract> contracts,
        List<AuthoringDiagnostic> diagnostics)
    {
        var retainedIds = new HashSet<DefinitionPortId>();
        var ports = new DefinitionPort[contracts.Count];
        for (var index = 0; index < contracts.Count; index++)
        {
            var contract = contracts[index];
            var declaration = contract.Declaration;
            ValidateDisplayText(declaration.DisplayName, "portDisplayName", diagnostics);
            if (!Enum.IsDefined(declaration.Direction))
            {
                diagnostics.Add(MissingReference("portDirection"));
            }

            if (declaration.Width == 0)
            {
                diagnostics.Add(InvalidWidth(declaration.Width));
            }

            if (!Enum.IsDefined(declaration.Placement.Facing))
            {
                diagnostics.Add(InvalidCoordinate("definitionPortPlacement", "facing"));
            }

            var id = contract switch
            {
                RetainedDefinitionPortContract retained => retained.DefinitionPortId,
                NewDefinitionPortContract => DefinitionPortId.Create(),
                _ => throw new InvalidOperationException(
                    "The Definition Port Contract variant is undefined."),
            };
            if (contract is RetainedDefinitionPortContract)
            {
                var original = target.FindPort(id);
                if (!retainedIds.Add(id))
                {
                    diagnostics.Add(DuplicateId("definitionPort"));
                }
                else if (original is null)
                {
                    diagnostics.Add(MissingReference("definitionPort"));
                }
                else if (original.Direction != declaration.Direction
                    || original.Width != declaration.Width)
                {
                    diagnostics.Add(InvalidParameter(
                        new CircuitDefinitionComponentTarget(target.Id),
                        "retainedPortSchemaChanged"));
                }
            }

            ports[index] = new DefinitionPort(
                id,
                declaration.DisplayName,
                declaration.Direction,
                declaration.Width,
                declaration.Placement);
        }

        return ports;
    }

    private static Dictionary<(CircuitDefinitionId, ComponentInstanceId),
        Dictionary<string, string?>>? ValidateCallSiteMigrations(
        CircuitDefinition target,
        DefinitionPort[] newPorts,
        (CircuitDefinition Definition, ComponentInstance Instance)[] callSites,
        ReadOnlyCollection<CallSiteTerminalMigration> requested,
        List<AuthoringDiagnostic> diagnostics)
    {
        var result = new Dictionary<(CircuitDefinitionId, ComponentInstanceId),
            Dictionary<string, string?>>();
        var callSiteKeys = callSites.Select(item =>
            (item.Definition.Id, item.Instance.Id)).ToHashSet();
        foreach (var request in requested)
        {
            var key = (request.ContainingCircuitDefinitionId, request.ComponentInstanceId);
            if (!callSiteKeys.Contains(key))
            {
                diagnostics.Add(MissingReference("componentInstanceCallSite"));
                continue;
            }

            if (result.ContainsKey(key))
            {
                diagnostics.Add(DuplicateId("componentInstance"));
                continue;
            }

            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            var destinationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var migration in request.Ports)
            {
                var original = target.FindPort(migration.OldPortId);
                if (original is null || !map.TryAdd(original.Id.Value, null))
                {
                    diagnostics.Add(original is null
                        ? MissingReference("definitionPort")
                        : DuplicateId("definitionPort"));
                    continue;
                }

                if (migration.NewPortIndex is null)
                {
                    continue;
                }

                if (migration.NewPortIndex < 0
                    || migration.NewPortIndex >= newPorts.Length)
                {
                    diagnostics.Add(MissingReference("newDefinitionPort"));
                    continue;
                }

                var destination = newPorts[migration.NewPortIndex.Value];
                if (!destinationIds.Add(destination.Id.Value))
                {
                    diagnostics.Add(DuplicateId("newDefinitionPort"));
                }

                if (original.Direction != destination.Direction
                    || original.Width != destination.Width)
                {
                    diagnostics.Add(InvalidParameter(
                        new CircuitDefinitionComponentTarget(target.Id),
                        "terminalMigrationIncompatible"));
                }

                map[original.Id.Value] = destination.Id.Value;
            }

            if (map.Count != target.Ports.Count)
            {
                diagnostics.Add(MissingReference("callSitePortMigration"));
            }

            result.Add(key, map);
        }

        if (result.Count != callSites.Length)
        {
            diagnostics.Add(MissingReference("callSiteMigration"));
        }

        return diagnostics.Count == 0 ? result : null;
    }

    private static Dictionary<string, string?>? ValidateInstancePortMigration(
        IReadOnlyList<ResolvedAuthoringPort> oldPorts,
        IReadOnlyList<ResolvedAuthoringPort> newPorts,
        IReadOnlyList<InstancePortMigration> requested,
        List<AuthoringDiagnostic> diagnostics)
    {
        var oldById = oldPorts.ToDictionary(port => port.Id, StringComparer.Ordinal);
        var newById = newPorts.ToDictionary(port => port.Id, StringComparer.Ordinal);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var migration in requested)
        {
            if (!oldById.TryGetValue(migration.OldPortId, out var oldPort))
            {
                diagnostics.Add(MissingReference("instancePort"));
                continue;
            }

            if (!result.TryAdd(migration.OldPortId, migration.NewPortId))
            {
                diagnostics.Add(DuplicateId("instancePort"));
                continue;
            }

            if (migration.NewPortId is null)
            {
                continue;
            }

            if (!newById.TryGetValue(migration.NewPortId, out var newPort))
            {
                diagnostics.Add(MissingReference("newInstancePort"));
            }
            else if (!destinations.Add(migration.NewPortId))
            {
                diagnostics.Add(DuplicateId("newInstancePort"));
            }
            else if (oldPort.Direction != newPort.Direction || oldPort.Width != newPort.Width)
            {
                diagnostics.Add(InvalidParameterToken("terminalMigrationIncompatible"));
            }
        }

        if (result.Count != oldPorts.Count)
        {
            diagnostics.Add(MissingReference("instancePortMigration"));
        }

        return diagnostics.Count == 0 ? result : null;
    }

    private static TerminalMigrationResult MigrateInstanceTerminals(
        CircuitDefinition definition,
        ComponentInstanceId instanceId,
        Dictionary<string, string?> migration)
    {
        return RewriteTerminals(definition, terminal =>
        {
            if (terminal is not InstanceTerminalReference instance
                || instance.ComponentInstanceId != instanceId)
            {
                return terminal;
            }

            var destination = migration[instance.PortId];
            return destination is null
                ? null
                : new InstanceTerminalReference(
                    definition.Id,
                    instanceId,
                    destination);
        });
    }

    private static TerminalMigrationResult RemoveObsoleteDefinitionTerminals(
        CircuitDefinition definition,
        HashSet<DefinitionPortId> retainedPortIds)
    {
        return RewriteTerminals(definition, terminal =>
            terminal is DefinitionTerminalReference boundary
                && !retainedPortIds.Contains(boundary.DefinitionPortId)
                ? null
                : terminal);
    }

    private static TerminalMigrationResult RewriteTerminals(
        CircuitDefinition definition,
        Func<AuthoredTerminalReference, AuthoredTerminalReference?> rewrite)
    {
        var removed = new List<NetId>();
        var changed = new List<NetId>();
        var nets = new List<Net>();
        foreach (var net in definition.Nets)
        {
            var terminals = net.Terminals.Select(rewrite)
                .Where(terminal => terminal is not null)
                .Cast<AuthoredTerminalReference>()
                .ToArray();
            var membershipChanged = !net.Terminals.SequenceEqual(terminals);
            var hasGeometry = definition.WireGeometries.Any(geometry =>
                geometry.NetId == net.Id);
            if (terminals.Length == 0 && net.JunctionIds.Count == 0 && !hasGeometry)
            {
                removed.Add(net.Id);
            }
            else
            {
                nets.Add(membershipChanged
                    ? net.WithMembership(terminals, net.JunctionIds.ToArray())
                    : net);
                if (membershipChanged)
                {
                    changed.Add(net.Id);
                }
            }
        }

        return new TerminalMigrationResult(
            nets.ToArray(),
            changed.ToArray(),
            removed.ToArray());
    }

    private static ResolvedAuthoringPort[] ResolveTargetPorts(
        ProjectDocument document,
        ComponentTarget target,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        List<AuthoringDiagnostic> diagnostics)
    {
        switch (target)
        {
            case LibraryComponentTarget library:
                var schema = document.LibrarySnapshot.ResolveContract(library.ContractKey);
                if (schema is null)
                {
                    diagnostics.Add(MissingReference("componentContract"));
                    return [];
                }

                var parameterDiagnostics = ComponentParameterValidator.Validate(
                    library.ContractKey,
                    schema,
                    parameters,
                    document: document);
                diagnostics.AddRange(parameterDiagnostics);
                if (parameterDiagnostics.Length != 0)
                {
                    return [];
                }

                var resolution = schema.ResolvePorts(parameters);
                if (!resolution.TryGetPortCount(out var portCount)
                    || portCount == 0
                    || !resolution.TryMaterialize(portCount, out var ports))
                {
                    diagnostics.Add(InvalidParameter(target, "portCount"));
                    return [];
                }

                return ports.Select(port => new ResolvedAuthoringPort(
                    port.Id,
                    port.Direction,
                    port.Width)).ToArray();
            case CircuitDefinitionComponentTarget definitionTarget:
                var definition = document.FindCircuitDefinition(
                    definitionTarget.CircuitDefinitionId);
                if (definition is null)
                {
                    diagnostics.Add(MissingReference("circuitDefinitionTarget"));
                    return [];
                }

                if (parameters.Count != 0)
                {
                    diagnostics.Add(InvalidParameter(target, "definitionParametersEmpty"));
                    return [];
                }

                return definition.Ports.Select(port => new ResolvedAuthoringPort(
                    port.Id.Value,
                    port.Direction,
                    port.Width)).ToArray();
            default:
                throw new InvalidOperationException(
                    "The Component Target variant is undefined.");
        }
    }

    private static bool PortSchemasMatch(
        IReadOnlyList<ResolvedAuthoringPort> left,
        IReadOnlyList<ResolvedAuthoringPort> right)
    {
        return left.SequenceEqual(right);
    }

    private static void ValidateSymbolVariant(
        SymbolProfileReference profile,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        string? variantId,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (variantId is null
            || SymbolVariantCatalog.IsCompatible(profile, target, parameters, variantId))
        {
            return;
        }

        diagnostics.Add(new AuthoringDiagnostic(
            "authoring_symbol_variant_incompatible",
            [
                new AuthoringDiagnosticArgument(
                    "variantId",
                    new StableTokenDiagnosticValue(IsStableName(variantId)
                        ? variantId
                        : "invalid")),
                new AuthoringDiagnosticArgument(
                    "contractKey",
                    new ContractKeyDiagnosticValue(TargetContractKey(target))),
            ]));
    }

    private static ComponentContractKey TargetContractKey(ComponentTarget target)
    {
        return target switch
        {
            LibraryComponentTarget library => library.ContractKey,
            CircuitDefinitionComponentTarget definition => new ComponentContractKey(
                "logiclab.project",
                definition.CircuitDefinitionId.Value),
            _ => throw new InvalidOperationException(
                "The Component Target variant is undefined."),
        };
    }

    private static AuthoringDiagnostic InvalidParameter(
        ComponentTarget target,
        string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_parameter",
            [
                new AuthoringDiagnosticArgument(
                    "contractKey",
                    new ContractKeyDiagnosticValue(TargetContractKey(target))),
                new AuthoringDiagnosticArgument(
                    "parameterId",
                    new StableTokenDiagnosticValue("migration")),
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]);
    }

    private static AuthoringDiagnostic InvalidParameterToken(string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_parameter",
            [
                new AuthoringDiagnosticArgument(
                    "contractKey",
                    new ContractKeyDiagnosticValue(new ComponentContractKey(
                        "logiclab.project",
                        "migration"))),
                new AuthoringDiagnosticArgument(
                    "parameterId",
                    new StableTokenDiagnosticValue("migration")),
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]);
    }

    private static AuthoringDiagnostic DeleteHasDependents(
        string dependentKind,
        int dependentCount)
    {
        return new AuthoringDiagnostic(
            "authoring_delete_has_dependents",
            [
                new AuthoringDiagnosticArgument(
                    "dependentKind",
                    new StableTokenDiagnosticValue(dependentKind)),
                new AuthoringDiagnosticArgument(
                    "dependentCount",
                    new UnsignedDecimalDiagnosticValue(checked((ulong)dependentCount))),
            ]);
    }

    private static IEnumerable<AuthoredSourceIdentity> DefinitionSources(
        CircuitDefinition definition)
    {
        yield return new CircuitRootSourceIdentity(definition.Id);
        foreach (var port in definition.Ports)
        {
            yield return new DefinitionPortSourceIdentity(definition.Id, port.Id);
        }

        foreach (var instance in definition.ComponentInstances)
        {
            yield return new ComponentInstanceSourceIdentity(definition.Id, instance.Id);
        }

        foreach (var net in definition.Nets)
        {
            yield return new NetSourceIdentity(definition.Id, net.Id);
        }

        foreach (var junction in definition.Junctions)
        {
            yield return new JunctionSourceIdentity(definition.Id, junction.Id);
        }

        foreach (var geometry in definition.WireGeometries)
        {
            yield return new WireGeometrySourceIdentity(definition.Id, geometry.Id);
        }

        foreach (var annotation in definition.Annotations)
        {
            yield return new AnnotationSourceIdentity(definition.Id, annotation.Id);
        }
    }

    private sealed record ResolvedAuthoringPort(
        string Id,
        PortDirection Direction,
        uint Width);

    private sealed record TerminalMigrationResult(
        Net[] Nets,
        NetId[] ChangedNetIds,
        NetId[] RemovedNetIds);
}
