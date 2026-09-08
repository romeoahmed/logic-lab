using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

internal sealed class CompilationSourceComparer : IComparer<CompilationSource>
{
    public static CompilationSourceComparer Instance { get; } = new();

    public int Compare(CompilationSource? left, CompilationSource? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        // Circuit roots precede entities; each group then follows declared source fields.
        var variantComparison = (left.Identity is not CircuitRootSourceIdentity)
            .CompareTo(right.Identity is not CircuitRootSourceIdentity);
        if (variantComparison != 0)
        {
            return variantComparison;
        }

        var circuitComparison = string.CompareOrdinal(
            left.Identity.CircuitDefinitionId.Value,
            right.Identity.CircuitDefinitionId.Value);
        if (circuitComparison != 0)
        {
            return circuitComparison;
        }

        var pathComparison = ComparePaths(left.HierarchyPath, right.HierarchyPath);
        if (pathComparison != 0)
        {
            return pathComparison;
        }

        if (left.Identity is CircuitRootSourceIdentity)
        {
            return 0;
        }

        var kindComparison = CircuitEntityKind(left.Identity)
            .CompareTo(CircuitEntityKind(right.Identity));
        if (kindComparison != 0)
        {
            return kindComparison;
        }

        var entityComparison = string.CompareOrdinal(
            CircuitEntityId(left.Identity),
            CircuitEntityId(right.Identity));
        return entityComparison != 0
            ? entityComparison
            : string.CompareOrdinal(
                (left.Identity as InstancePortSourceIdentity)?.PortId,
                (right.Identity as InstancePortSourceIdentity)?.PortId);
    }

    private static int ComparePaths(HierarchyPath left, HierarchyPath right)
    {
        var entryComparison = string.CompareOrdinal(
            left.EntryCircuitDefinitionId.Value,
            right.EntryCircuitDefinitionId.Value);
        if (entryComparison != 0)
        {
            return entryComparison;
        }

        var commonCount = Math.Min(left.Steps.Count, right.Steps.Count);
        for (var index = 0; index < commonCount; index++)
        {
            var definitionComparison = string.CompareOrdinal(
                left.Steps[index].ContainingCircuitDefinitionId.Value,
                right.Steps[index].ContainingCircuitDefinitionId.Value);
            if (definitionComparison != 0)
            {
                return definitionComparison;
            }

            var instanceComparison = string.CompareOrdinal(
                left.Steps[index].ComponentInstanceId.Value,
                right.Steps[index].ComponentInstanceId.Value);
            if (instanceComparison != 0)
            {
                return instanceComparison;
            }
        }

        return left.Steps.Count.CompareTo(right.Steps.Count);
    }

    private static int CircuitEntityKind(CircuitSourceIdentity identity)
    {
        return identity switch
        {
            DefinitionPortSourceIdentity => 0,
            ComponentInstanceSourceIdentity or InstancePortSourceIdentity => 1,
            NetSourceIdentity => 2,
            JunctionSourceIdentity => 3,
            WireGeometrySourceIdentity => 4,
            AnnotationSourceIdentity => 5,
            _ => throw new InvalidOperationException(
                "The circuit entity Compilation Source Identity variant is undefined."),
        };
    }

    private static string CircuitEntityId(CircuitSourceIdentity identity)
    {
        return identity switch
        {
            DefinitionPortSourceIdentity source => source.DefinitionPortId.Value,
            ComponentInstanceSourceIdentity source => source.ComponentInstanceId.Value,
            InstancePortSourceIdentity source => source.ComponentInstanceId.Value,
            NetSourceIdentity source => source.NetId.Value,
            JunctionSourceIdentity source => source.JunctionId.Value,
            WireGeometrySourceIdentity source => source.WireGeometryId.Value,
            AnnotationSourceIdentity source => source.AnnotationId.Value,
            _ => throw new InvalidOperationException(
                "The circuit entity Compilation Source Identity variant is undefined."),
        };
    }
}
