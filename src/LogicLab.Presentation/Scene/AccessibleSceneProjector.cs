using LogicLab.Domain.Authoring;

namespace LogicLab.Presentation.Scene;

public static class AccessibleSceneProjector
{
    public static AccessibleSceneProjection Project(ProjectRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return Project(revision, revision.Document.EntryCircuitDefinitionId);
    }

    public static AccessibleSceneProjection Project(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId)
            ?? throw new ArgumentException(
                "The selected Circuit Definition does not exist in the Project Revision.",
                nameof(circuitDefinitionId));
        var definitionPorts = definition.Ports
            .Select(port => new AccessibleDefinitionPortProjection(
                new DefinitionPortSourceIdentity(definition.Id, port.Id),
                port.DisplayName,
                port.Direction,
                port.Width,
                port.Placement))
            .ToArray();
        var components = definition.ComponentInstances
            .Select(instance => ProjectComponent(revision, definition.Id, instance))
            .OrderBy(item => item.Placement.Origin.X)
            .ThenBy(item => item.Placement.Origin.Y)
            .ThenBy(item => item.Source.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        var connections = definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .Select(net => ProjectConnection(definition, net))
            .ToArray();

        return new AccessibleSceneProjection(
            definition.Id,
            definition.DisplayName,
            definitionPorts,
            components,
            connections);
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
        ComponentInstance instance)
    {
        var (label, ports) = instance.Target switch
        {
            LibraryComponentTarget library => ProjectLibraryComponent(
                revision,
                definitionId,
                instance,
                library),
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
            LibraryComponentTarget target)
    {
        var schema = revision.Document.LibrarySnapshot.ResolveContract(target.ContractKey)
            ?? throw new InvalidOperationException(
                "The authored component contract is absent from the pinned Library Snapshot.");
        var ports = schema.ResolvePorts(instance.Parameters)
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
        var targetDefinition = revision.Document.FindCircuitDefinition(
            target.CircuitDefinitionId)
            ?? throw new InvalidOperationException(
                "The authored Circuit Definition target is absent from the Project Revision.");
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

    private static string ContractLabel(string contractId)
    {
        return contractId switch
        {
            "source.input" => "Input",
            "source.constant" => "Constant",
            "logic.not" => "NOT",
            "sink.output" => "Output",
            "topology.split" => "Split",
            "topology.concat" => "Concat",
            "topology.zero_extend" => "Zero Extend",
            "topology.sign_extend" => "Sign Extend",
            _ => contractId,
        };
    }
}
