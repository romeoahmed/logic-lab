using System.Diagnostics.CodeAnalysis;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.Scene;

public static class AccessibleSceneProjector
{
    public static bool TryProject(
        ProjectRevision revision,
        ulong maximumPortCount,
        [NotNullWhen(true)] out AccessibleSceneProjection? projection)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return TryProject(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            maximumPortCount,
            out projection);
    }

    public static bool TryProject(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId,
        ulong maximumPortCount,
        [NotNullWhen(true)] out AccessibleSceneProjection? projection)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId)
            ?? throw new ArgumentException(
                "The selected Circuit Definition does not exist in the Project Revision.",
                nameof(circuitDefinitionId));
        if (!FitsPortBudget(revision, definition, maximumPortCount))
        {
            projection = null;
            return false;
        }

        var definitionPorts = definition.Ports
            .Select(port => new AccessibleDefinitionPortProjection(
                new DefinitionPortSourceIdentity(definition.Id, port.Id),
                port.DisplayName,
                port.Direction,
                port.Width,
                port.Placement))
            .ToArray();
        var components = definition.ComponentInstances
            .OrderBy(item => item.Placement.Origin.X)
            .ThenBy(item => item.Placement.Origin.Y)
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(instance => ProjectComponent(
                revision,
                definition.Id,
                instance,
                maximumPortCount))
            .ToArray();
        var connections = definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .Select(net => ProjectConnection(definition, net))
            .ToArray();
        var annotations = definition.Annotations
            .OrderBy(annotation => annotation.Position.X)
            .ThenBy(annotation => annotation.Position.Y)
            .ThenBy(annotation => annotation.Id.Value, StringComparer.Ordinal)
            .Select(annotation => new AccessibleAnnotationProjection(
                new AnnotationSourceIdentity(definition.Id, annotation.Id),
                annotation.Text,
                annotation.Position))
            .ToArray();

        projection = new AccessibleSceneProjection(
            definition.Id,
            definition.DisplayName,
            definitionPorts,
            components,
            connections,
            annotations);
        return true;
    }

    private static bool FitsPortBudget(
        ProjectRevision revision,
        CircuitDefinition definition,
        ulong maximumPortCount)
    {
        var remaining = maximumPortCount;
        if (!TryConsume(ref remaining, checked((ulong)definition.Ports.Count)))
        {
            return false;
        }

        foreach (var instance in definition.ComponentInstances)
        {
            ulong portCount;
            switch (instance.Target)
            {
                case LibraryComponentTarget library:
                    var schema = ResolveContract(revision, library);
                    var resolution = schema.ResolvePorts(instance.Parameters);
                    if (!resolution.TryGetPortCount(out portCount)
                        || portCount > int.MaxValue)
                    {
                        return false;
                    }
                    break;
                case CircuitDefinitionComponentTarget definitionTarget:
                    var targetDefinition = ResolveDefinition(revision, definitionTarget);
                    portCount = checked((ulong)targetDefinition.Ports.Count);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Component Target variant is undefined.");
            }

            if (!TryConsume(ref remaining, portCount))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryConsume(ref ulong remaining, ulong count)
    {
        if (count > remaining)
        {
            return false;
        }

        remaining -= count;
        return true;
    }

    private static AccessibleConnectionProjection ProjectConnection(
        CircuitDefinition definition,
        Net net)
    {
        var netSource = new NetSourceIdentity(definition.Id, net.Id);
        var junctions = definition.Junctions
            .Where(junction => junction.NetId == net.Id)
            .OrderBy(junction => junction.Id.Value, StringComparer.Ordinal)
            .Select(junction => new AccessibleJunctionProjection(
                new JunctionSourceIdentity(definition.Id, junction.Id),
                netSource,
                junction.Position))
            .ToArray();
        var wireGeometries = definition.WireGeometries
            .Where(geometry => geometry.NetId == net.Id)
            .OrderBy(geometry => geometry.Id.Value, StringComparer.Ordinal)
            .Select(geometry => new AccessibleWireGeometryProjection(
                new WireGeometrySourceIdentity(definition.Id, geometry.Id),
                netSource,
                geometry.Route))
            .ToArray();
        return new AccessibleConnectionProjection(
            netSource,
            net.Width,
            net.Terminals,
            junctions,
            wireGeometries);
    }

    private static AccessibleComponentProjection ProjectComponent(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        ulong maximumPortCount)
    {
        var (label, ports) = instance.Target switch
        {
            LibraryComponentTarget library => ProjectLibraryComponent(
                revision,
                definitionId,
                instance,
                library,
                maximumPortCount),
            CircuitDefinitionComponentTarget definition => ProjectDefinitionComponent(
                revision,
                definitionId,
                instance,
                definition),
            _ => throw new InvalidOperationException(
                "The Component Target variant is undefined."),
        };

        return new AccessibleComponentProjection(
            new ComponentInstanceSourceIdentity(definitionId, instance.Id),
            label,
            instance.Placement,
            ports);
    }

    private static (string Label, AccessiblePortProjection[] Ports)
        ProjectLibraryComponent(
            ProjectRevision revision,
            CircuitDefinitionId definitionId,
            ComponentInstance instance,
            LibraryComponentTarget target,
            ulong maximumPortCount)
    {
        var schema = ResolveContract(revision, target);
        var resolution = schema.ResolvePorts(instance.Parameters);
        if (!resolution.TryMaterialize(maximumPortCount, out var resolvedPorts))
        {
            throw new InvalidOperationException(
                "A preflight-admitted component Port resolution could not be materialized.");
        }

        var ports = resolvedPorts
            .Select(port => new AccessiblePortProjection(
                new InstancePortSourceIdentity(definitionId, instance.Id, port.Id),
                port.Id,
                port.Direction,
                port.Width))
            .ToArray();
        return (
            instance.DisplayName ?? ContractLabel(target.ContractKey.ContractId),
            ports);
    }

    private static (string Label, AccessiblePortProjection[] Ports)
        ProjectDefinitionComponent(
            ProjectRevision revision,
            CircuitDefinitionId definitionId,
            ComponentInstance instance,
            CircuitDefinitionComponentTarget target)
    {
        var targetDefinition = ResolveDefinition(revision, target);
        var ports = targetDefinition.Ports
            .Select(port => new AccessiblePortProjection(
                new InstancePortSourceIdentity(
                    definitionId,
                    instance.Id,
                    port.Id.Value),
                port.DisplayName,
                port.Direction,
                port.Width))
            .ToArray();
        return (instance.DisplayName ?? targetDefinition.DisplayName, ports);
    }

    private static ComponentContractSchema ResolveContract(
        ProjectRevision revision,
        LibraryComponentTarget target)
    {
        return revision.Document.LibrarySnapshot.ResolveContract(target.ContractKey)
            ?? throw new InvalidOperationException(
                "The authored component contract is absent from the pinned Library Snapshot.");
    }

    private static CircuitDefinition ResolveDefinition(
        ProjectRevision revision,
        CircuitDefinitionComponentTarget target)
    {
        return revision.Document.FindCircuitDefinition(target.CircuitDefinitionId)
            ?? throw new InvalidOperationException(
                "The authored Circuit Definition target is absent from the Project Revision.");
    }

    private static string ContractLabel(string contractId)
    {
        return contractId switch
        {
            "source.input" => "Input",
            "source.constant" => "Constant",
            "logic.not" => "NOT",
            "logic.and" => "AND",
            "logic.adder" => "Adder",
            "logic.buffer" => "Buffer",
            "logic.decoder" => "Decoder",
            "logic.demux" => "DEMUX",
            "logic.mux" => "MUX",
            "logic.nand" => "NAND",
            "logic.nor" => "NOR",
            "logic.or" => "OR",
            "logic.priority_encoder" => "Priority Encoder",
            "logic.shift" => "Logical Shift",
            "logic.subtractor" => "Subtractor",
            "logic.tristate" => "Tri-State",
            "logic.unsigned_compare" => "Unsigned Compare",
            "logic.xnor" => "XNOR",
            "logic.xor" => "XOR",
            "sink.output" => "Output",
            "topology.split" => "Split",
            "topology.concat" => "Concat",
            "topology.zero_extend" => "Zero Extend",
            "topology.sign_extend" => "Sign Extend",
            _ => contractId,
        };
    }

}
