using LogicLab.Domain.Authoring;

namespace LogicLab.Presentation.Scene;

public static class AccessibleSceneProjector
{
    public static AccessibleSceneProjection Project(
        ProjectRevision revision,
        ulong maximumPortCount)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return Project(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            maximumPortCount);
    }

    public static AccessibleSceneProjection Project(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId,
        ulong maximumPortCount)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId)
            ?? throw new ArgumentException(
                "The selected Circuit Definition does not exist in the Project Revision.",
                nameof(circuitDefinitionId));
        var budget = new PortProjectionBudget(maximumPortCount);
        budget.Consume(checked((ulong)definition.Ports.Count));
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
                budget))
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
        ComponentInstance instance,
        PortProjectionBudget budget)
    {
        var (label, ports) = instance.Target switch
        {
            LibraryComponentTarget library => ProjectLibraryComponent(
                revision,
                definitionId,
                instance,
                library,
                budget),
            CircuitDefinitionComponentTarget definition => ProjectDefinitionComponent(
                revision,
                definitionId,
                instance,
                definition,
                budget),
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
            PortProjectionBudget budget)
    {
        var schema = revision.Document.LibrarySnapshot.ResolveContract(target.ContractKey)
            ?? throw new InvalidOperationException(
                "The authored component contract is absent from the pinned Library Snapshot.");
        var resolution = schema.PreparePorts(instance.Parameters);
        if (budget.Remaining == 0
            || !resolution.TryMaterialize(budget.Remaining, out var resolvedPorts))
        {
            throw new AccessibleSceneProjectionLimitExceededException(budget.Maximum);
        }

        budget.Consume(checked((ulong)resolvedPorts.Count));
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
            CircuitDefinitionComponentTarget target,
            PortProjectionBudget budget)
    {
        var targetDefinition = revision.Document.FindCircuitDefinition(
            target.CircuitDefinitionId)
            ?? throw new InvalidOperationException(
                "The authored Circuit Definition target is absent from the Project Revision.");
        budget.Consume(checked((ulong)targetDefinition.Ports.Count));
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
            "logic.and" => "AND",
            "logic.buffer" => "Buffer",
            "logic.decoder" => "Decoder",
            "logic.demux" => "DEMUX",
            "logic.mux" => "MUX",
            "logic.nand" => "NAND",
            "logic.nor" => "NOR",
            "logic.or" => "OR",
            "logic.priority_encoder" => "Priority Encoder",
            "logic.tristate" => "Tri-State",
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

    private sealed class PortProjectionBudget(ulong maximum)
    {
        public ulong Maximum { get; } = maximum;

        public ulong Remaining { get; private set; } = maximum;

        public void Consume(ulong count)
        {
            if (count > Remaining)
            {
                throw new AccessibleSceneProjectionLimitExceededException(Maximum);
            }

            Remaining -= count;
        }
    }
}

public sealed class AccessibleSceneProjectionLimitExceededException(
    ulong maximumPortCount) : Exception(
        "The accessible Scene Port count exceeds the active projection budget.")
{
    public ulong MaximumPortCount { get; } = maximumPortCount;
}
