using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public sealed class ProjectRevision
{
    internal ProjectRevision(ProjectRevisionId revisionId, ProjectDocument document)
    {
        RevisionId = revisionId;
        Document = document;
    }

    public ProjectRevisionId RevisionId { get; }

    public ProjectDocument Document { get; }
}

public sealed class ProjectDocument
{
    private readonly CircuitDefinition[] circuitDefinitions;

    internal ProjectDocument(
        ProjectId projectId,
        string displayName,
        LibrarySnapshot librarySnapshot,
        SymbolProfileReference symbolProfile,
        CircuitDefinitionId entryCircuitDefinitionId,
        CircuitDefinition[] circuitDefinitions)
    {
        ProjectId = projectId;
        DisplayName = displayName;
        LibrarySnapshot = librarySnapshot;
        SymbolProfile = symbolProfile;
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        this.circuitDefinitions = (CircuitDefinition[])circuitDefinitions.Clone();
        CircuitDefinitions = Array.AsReadOnly(this.circuitDefinitions);
    }

    public ProjectId ProjectId { get; }

    public string DisplayName { get; }

    public LibrarySnapshot LibrarySnapshot { get; }

    public SymbolProfileReference SymbolProfile { get; }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public ReadOnlyCollection<CircuitDefinition> CircuitDefinitions { get; }

    public CircuitDefinition EntryCircuitDefinition =>
        FindCircuitDefinition(EntryCircuitDefinitionId)
        ?? throw new InvalidOperationException(
            "The entry Circuit Definition is missing from the Project Document.");

    public CircuitDefinition? FindCircuitDefinition(CircuitDefinitionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(circuitDefinitions, definition => definition.Id == id);
    }

    internal ProjectDocument ReplaceCircuitDefinition(CircuitDefinition replacement)
    {
        var definitions = (CircuitDefinition[])circuitDefinitions.Clone();
        var index = Array.FindIndex(
            definitions,
            definition => definition.Id == replacement.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(
                "The replacement Circuit Definition does not belong to this Project Document.");
        }

        definitions[index] = replacement;
        Array.Sort(
            definitions,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));

        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            definitions);
    }
}

public sealed class CircuitDefinition
{
    private readonly ComponentInstance[] componentInstances;
    private readonly Net[] nets;

    internal CircuitDefinition(
        CircuitDefinitionId id,
        string displayName,
        ComponentInstance[] componentInstances,
        Net[] nets)
    {
        Id = id;
        DisplayName = displayName;
        this.componentInstances = (ComponentInstance[])componentInstances.Clone();
        this.nets = (Net[])nets.Clone();
        ComponentInstances = Array.AsReadOnly(this.componentInstances);
        Nets = Array.AsReadOnly(this.nets);
    }

    public CircuitDefinitionId Id { get; }

    public string DisplayName { get; }

    public ReadOnlyCollection<ComponentInstance> ComponentInstances { get; }

    public ReadOnlyCollection<Net> Nets { get; }

    public ComponentInstance? FindComponentInstance(ComponentInstanceId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(componentInstances, instance => instance.Id == id);
    }

    public Net? FindNet(NetId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(nets, net => net.Id == id);
    }

    internal CircuitDefinition AddComponentInstance(ComponentInstance instance)
    {
        var instances = new ComponentInstance[componentInstances.Length + 1];
        componentInstances.CopyTo(instances, 0);
        instances[^1] = instance;
        Array.Sort(
            instances,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));

        return new CircuitDefinition(Id, DisplayName, instances, nets);
    }

    internal CircuitDefinition ReplaceComponentInstances(ComponentInstance[] replacements)
    {
        var replacementById = replacements.ToDictionary(instance => instance.Id);
        var instances = componentInstances
            .Select(instance => replacementById.GetValueOrDefault(instance.Id, instance))
            .ToArray();
        return new CircuitDefinition(Id, DisplayName, instances, nets);
    }

    internal CircuitDefinition AddNet(Net net)
    {
        var updatedNets = new Net[nets.Length + 1];
        nets.CopyTo(updatedNets, 0);
        updatedNets[^1] = net;
        Array.Sort(
            updatedNets,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new CircuitDefinition(Id, DisplayName, componentInstances, updatedNets);
    }
}

public abstract record ComponentParameterValue
{
    private protected ComponentParameterValue()
    {
    }
}

public sealed record Unsigned32ParameterValue(uint Value) : ComponentParameterValue;

public sealed record ChoiceParameterValue(string Value) : ComponentParameterValue;

public sealed record LogicVectorParameterValue : ComponentParameterValue
{
    private readonly LogicValue[] values;

    public LogicVectorParameterValue(IReadOnlyList<LogicValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = values.ToArray();
        Values = Array.AsReadOnly(this.values);
    }

    public ReadOnlyCollection<LogicValue> Values { get; }
}

public sealed record ComponentParameterBinding(
    string ParameterId,
    ComponentParameterValue Value);

public sealed class ComponentInstance
{
    private readonly ComponentParameterBinding[] parameters;

    internal ComponentInstance(
        ComponentInstanceId id,
        ComponentContractKey contractKey,
        ComponentParameterBinding[] parameters,
        ComponentPlacement placement,
        string? displayName)
    {
        Id = id;
        ContractKey = contractKey;
        this.parameters = (ComponentParameterBinding[])parameters.Clone();
        Parameters = Array.AsReadOnly(this.parameters);
        Placement = placement;
        DisplayName = displayName;
    }

    public ComponentInstanceId Id { get; }

    public ComponentContractKey ContractKey { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ComponentPlacement Placement { get; }

    public string? DisplayName { get; }

    internal ComponentInstance WithPlacement(ComponentPlacement placement)
    {
        return new ComponentInstance(
            Id,
            ContractKey,
            parameters,
            placement,
            DisplayName);
    }
}

public sealed record InstanceTerminalReference(
    CircuitDefinitionId CircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId,
    string PortId);

public sealed class Net
{
    private readonly InstanceTerminalReference[] terminals;

    internal Net(
        NetId id,
        uint width,
        InstanceTerminalReference[] terminals)
    {
        Id = id;
        Width = width;
        this.terminals = (InstanceTerminalReference[])terminals.Clone();
        Terminals = Array.AsReadOnly(this.terminals);
    }

    public NetId Id { get; }

    public uint Width { get; }

    public ReadOnlyCollection<InstanceTerminalReference> Terminals { get; }
}
