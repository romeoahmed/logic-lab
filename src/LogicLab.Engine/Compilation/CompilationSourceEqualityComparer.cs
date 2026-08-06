namespace LogicLab.Engine.Compilation;

internal sealed class CompilationSourceEqualityComparer : IEqualityComparer<CompilationSource>
{
    public static CompilationSourceEqualityComparer Instance { get; } = new();

    public bool Equals(CompilationSource? left, CompilationSource? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.Identity == right.Identity
            && left.HierarchyPath.EntryCircuitDefinitionId
                == right.HierarchyPath.EntryCircuitDefinitionId
            && left.HierarchyPath.Steps.SequenceEqual(right.HierarchyPath.Steps);
    }

    public int GetHashCode(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var hash = new HashCode();
        hash.Add(source.Identity);
        hash.Add(source.HierarchyPath.EntryCircuitDefinitionId);
        foreach (var step in source.HierarchyPath.Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }
}
