using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

public sealed record HierarchyPathStep(
    CircuitDefinitionId ContainingCircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId);

public sealed record HierarchyPath
{
    public HierarchyPath(
        CircuitDefinitionId entryCircuitDefinitionId,
        IReadOnlyList<HierarchyPathStep> steps)
    {
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionId);
        ArgumentNullException.ThrowIfNull(steps);
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        Steps = Array.AsReadOnly(steps.ToArray());
    }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public ReadOnlyCollection<HierarchyPathStep> Steps { get; }

    public bool Equals(HierarchyPath? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && EntryCircuitDefinitionId == other.EntryCircuitDefinitionId
                && Steps.SequenceEqual(other.Steps));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EntryCircuitDefinitionId);
        foreach (var step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }
}

public sealed record CompilationSource
{
    public CompilationSource(
        CircuitSourceIdentity identity,
        HierarchyPath hierarchyPath)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(hierarchyPath);
        Identity = identity;
        HierarchyPath = hierarchyPath;
    }

    public CircuitSourceIdentity Identity { get; }

    public HierarchyPath HierarchyPath { get; }
}
