using LogicLab.Domain.Authoring;

namespace LogicLab.Presentation.Scene;

public static class AccessibleSceneProjector
{
    public static AccessibleSceneProjection Project(ProjectRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var definition = revision.Document.EntryCircuitDefinition;
        var components = definition.ComponentInstances
            .Select(instance => ProjectComponent(revision, definition.Id, instance))
            .OrderBy(item => item.Placement.Origin.X)
            .ThenBy(item => item.Placement.Origin.Y)
            .ThenBy(item => item.Source.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        var connections = definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .Select(net => new AccessibleConnectionProjection(
                new NetSourceIdentity(definition.Id, net.Id),
                net.Width,
                net.Terminals))
            .ToArray();

        return new AccessibleSceneProjection(
            definition.Id,
            definition.DisplayName,
            components,
            connections);
    }

    private static AccessibleComponentProjection ProjectComponent(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        ComponentInstance instance)
    {
        var schema = revision.Document.LibrarySnapshot.ResolveContract(instance.ContractKey)
            ?? throw new InvalidOperationException(
                "The authored component contract is absent from the pinned Library Snapshot.");
        var ports = schema.Ports
            .Select(port => new AccessiblePortProjection(
                new InstancePortSourceIdentity(definitionId, instance.Id, port.Id),
                port.Id,
                port.Direction))
            .ToArray();

        return new AccessibleComponentProjection(
            new ComponentInstanceSourceIdentity(definitionId, instance.Id),
            instance.DisplayName ?? ContractLabel(instance.ContractKey.ContractId),
            instance.Placement,
            ports);
    }

    private static string ContractLabel(string contractId)
    {
        return contractId switch
        {
            "source.input" => "Input",
            "logic.not" => "NOT",
            "sink.output" => "Output",
            _ => contractId,
        };
    }
}
