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

        var variantComparison = SourceVariant(left.Identity)
            .CompareTo(SourceVariant(right.Identity));
        if (variantComparison != 0)
        {
            return variantComparison;
        }

        return (left.Identity, right.Identity) switch
        {
            (ProjectRootSourceIdentity l, ProjectRootSourceIdentity r) =>
                string.CompareOrdinal(l.ProjectId.Value, r.ProjectId.Value),
            (MemoryImageSourceIdentity l, MemoryImageSourceIdentity r) =>
                CompareMemoryImages(l, r),
            _ => CompareCircuitSources(left, right),
        };
    }

    private static int CompareMemoryImages(
        MemoryImageSourceIdentity left,
        MemoryImageSourceIdentity right)
    {
        var projectComparison = string.CompareOrdinal(
            left.ProjectId.Value,
            right.ProjectId.Value);
        return projectComparison != 0
            ? projectComparison
            : string.CompareOrdinal(
                left.MemoryImageId.Value,
                right.MemoryImageId.Value);
    }

    private static int CompareCircuitSources(
        CompilationSource left,
        CompilationSource right)
    {
        var circuitComparison = string.CompareOrdinal(
            CircuitDefinitionId(left.Identity).Value,
            CircuitDefinitionId(right.Identity).Value);
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
            : CompareOptionalPortIds(
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

    private static int CompareOptionalPortIds(string? left, string? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return right is null ? 1 : string.CompareOrdinal(left, right);
    }

    private static int SourceVariant(AuthoredSourceIdentity identity)
    {
        return identity switch
        {
            ProjectRootSourceIdentity => 0,
            MemoryImageSourceIdentity => 1,
            CircuitRootSourceIdentity => 2,
            DefinitionPortSourceIdentity or
                ComponentInstanceSourceIdentity or
                InstancePortSourceIdentity or
                NetSourceIdentity or
                JunctionSourceIdentity or
                WireGeometrySourceIdentity or
                AnnotationSourceIdentity => 3,
            _ => throw new InvalidOperationException(
                "The Compilation Source Location variant is undefined."),
        };
    }

    private static CircuitDefinitionId CircuitDefinitionId(
        AuthoredSourceIdentity identity)
    {
        return identity switch
        {
            CircuitRootSourceIdentity source => source.CircuitDefinitionId,
            DefinitionPortSourceIdentity source => source.CircuitDefinitionId,
            ComponentInstanceSourceIdentity source => source.CircuitDefinitionId,
            InstancePortSourceIdentity source => source.CircuitDefinitionId,
            NetSourceIdentity source => source.CircuitDefinitionId,
            JunctionSourceIdentity source => source.CircuitDefinitionId,
            WireGeometrySourceIdentity source => source.CircuitDefinitionId,
            AnnotationSourceIdentity source => source.CircuitDefinitionId,
            _ => throw new InvalidOperationException(
                "The circuit Compilation Source Identity variant is undefined."),
        };
    }

    private static int CircuitEntityKind(AuthoredSourceIdentity identity)
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

    private static string CircuitEntityId(AuthoredSourceIdentity identity)
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
