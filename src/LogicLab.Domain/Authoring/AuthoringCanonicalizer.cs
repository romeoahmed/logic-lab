using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

internal static class AuthoringCanonicalizer
{
    public static AuthoredSourceIdentity[] Sources(
        IEnumerable<AuthoredSourceIdentity> sources)
    {
        return [.. sources
            .Distinct()
            .OrderBy(source => source, AuthoredSourceIdentityComparer.Instance)];
    }

    public static AuthoringDiagnostic[] Diagnostics(
        IEnumerable<AuthoringDiagnostic> diagnostics)
    {
        var ordered = diagnostics
            .OrderBy(diagnostic => diagnostic, AuthoringDiagnosticComparer.Instance)
            .ToArray();
        if (ordered.Length < 2)
        {
            return ordered;
        }

        var canonical = new List<AuthoringDiagnostic>(ordered.Length)
        {
            ordered[0],
        };
        for (var index = 1; index < ordered.Length; index++)
        {
            if (AuthoringDiagnosticComparer.Instance.Compare(
                    ordered[index - 1],
                    ordered[index]) != 0)
            {
                canonical.Add(ordered[index]);
            }
        }

        return [.. canonical];
    }

    private sealed class AuthoringDiagnosticComparer : IComparer<AuthoringDiagnostic>
    {
        public static AuthoringDiagnosticComparer Instance { get; } = new();

        public int Compare(AuthoringDiagnostic? left, AuthoringDiagnostic? right)
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

            var primaryComparison = AuthoredSourceIdentityComparer.Instance.Compare(
                left.Primary,
                right.Primary);
            if (primaryComparison != 0)
            {
                return primaryComparison;
            }

            var codeComparison = string.CompareOrdinal(left.Code, right.Code);
            return codeComparison != 0
                ? codeComparison
                : CompareArguments(left.Arguments, right.Arguments);
        }

        private static int CompareArguments(
            ReadOnlyCollection<AuthoringDiagnosticArgument> left,
            ReadOnlyCollection<AuthoringDiagnosticArgument> right)
        {
            var commonCount = Math.Min(left.Count, right.Count);
            for (var index = 0; index < commonCount; index++)
            {
                var nameComparison = string.CompareOrdinal(
                    left[index].Name,
                    right[index].Name);
                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                var valueComparison = CompareValues(
                    left[index].Value,
                    right[index].Value);
                if (valueComparison != 0)
                {
                    return valueComparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private static int CompareValues(
            AuthoringDiagnosticValue left,
            AuthoringDiagnosticValue right)
        {
            var kindComparison = ValueKindOrder(left).CompareTo(ValueKindOrder(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (StableTokenDiagnosticValue l, StableTokenDiagnosticValue r) =>
                    string.CompareOrdinal(l.Value, r.Value),
                (UnsignedDecimalDiagnosticValue l, UnsignedDecimalDiagnosticValue r) =>
                    l.Value.CompareTo(r.Value),
                (ContractKeyDiagnosticValue l, ContractKeyDiagnosticValue r) =>
                    CompareContractKeys(l.Value, r.Value),
                _ => throw new InvalidOperationException(
                    "The Authoring Diagnostic Value variant is undefined."),
            };
        }

        private static int ValueKindOrder(AuthoringDiagnosticValue value)
        {
            return value switch
            {
                StableTokenDiagnosticValue => 0,
                UnsignedDecimalDiagnosticValue => 1,
                ContractKeyDiagnosticValue => 2,
                _ => throw new InvalidOperationException(
                    "The Authoring Diagnostic Value variant is undefined."),
            };
        }

        private static int CompareContractKeys(
            ComponentContractKey left,
            ComponentContractKey right)
        {
            var libraryComparison = string.CompareOrdinal(
                left.LibraryId,
                right.LibraryId);
            return libraryComparison != 0
                ? libraryComparison
                : string.CompareOrdinal(left.ContractId, right.ContractId);
        }
    }

    private sealed class AuthoredSourceIdentityComparer
        : IComparer<AuthoredSourceIdentity>
    {
        public static AuthoredSourceIdentityComparer Instance { get; } = new();

        public int Compare(AuthoredSourceIdentity? left, AuthoredSourceIdentity? right)
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

            var kindComparison = LocationVariant(left).CompareTo(LocationVariant(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (ProjectRootSourceIdentity l, ProjectRootSourceIdentity r) =>
                    string.CompareOrdinal(l.ProjectId.Value, r.ProjectId.Value),
                (MemoryImageSourceIdentity l, MemoryImageSourceIdentity r) =>
                    CompareProjectResources(l, r),
                (CircuitRootSourceIdentity l, CircuitRootSourceIdentity r) =>
                    string.CompareOrdinal(
                        l.CircuitDefinitionId.Value,
                        r.CircuitDefinitionId.Value),
                _ => CompareCircuitEntities(left, right),
            };
        }

        private static int LocationVariant(AuthoredSourceIdentity identity)
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
                    "The Authored Source Identity variant is undefined."),
            };
        }

        private static int CompareCircuitEntities(
            AuthoredSourceIdentity left,
            AuthoredSourceIdentity right)
        {
            var circuitComparison = string.CompareOrdinal(
                GetCircuitDefinitionId(left).Value,
                GetCircuitDefinitionId(right).Value);
            if (circuitComparison != 0)
            {
                return circuitComparison;
            }

            var kindComparison = CircuitEntityKind(left)
                .CompareTo(CircuitEntityKind(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            var entityComparison = string.CompareOrdinal(
                CircuitEntityId(left),
                CircuitEntityId(right));
            return entityComparison != 0
                ? entityComparison
                : CompareOptionalPortIds(
                    (left as InstancePortSourceIdentity)?.PortId,
                    (right as InstancePortSourceIdentity)?.PortId);
        }

        private static CircuitDefinitionId GetCircuitDefinitionId(
            AuthoredSourceIdentity identity)
        {
            return identity switch
            {
                DefinitionPortSourceIdentity source => source.CircuitDefinitionId,
                ComponentInstanceSourceIdentity source => source.CircuitDefinitionId,
                InstancePortSourceIdentity source => source.CircuitDefinitionId,
                NetSourceIdentity source => source.CircuitDefinitionId,
                JunctionSourceIdentity source => source.CircuitDefinitionId,
                WireGeometrySourceIdentity source => source.CircuitDefinitionId,
                AnnotationSourceIdentity source => source.CircuitDefinitionId,
                _ => throw new InvalidOperationException(
                    "The circuit Source Identity variant is undefined."),
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
                    "The circuit entity Source Identity variant is undefined."),
            };
        }

        private static string CircuitEntityId(AuthoredSourceIdentity identity)
        {
            return identity switch
            {
                DefinitionPortSourceIdentity source => source.DefinitionPortId.Value,
                ComponentInstanceSourceIdentity source =>
                    source.ComponentInstanceId.Value,
                InstancePortSourceIdentity source => source.ComponentInstanceId.Value,
                NetSourceIdentity source => source.NetId.Value,
                JunctionSourceIdentity source => source.JunctionId.Value,
                WireGeometrySourceIdentity source => source.WireGeometryId.Value,
                AnnotationSourceIdentity source => source.AnnotationId.Value,
                _ => throw new InvalidOperationException(
                    "The circuit entity Source Identity variant is undefined."),
            };
        }

        private static int CompareOptionalPortIds(string? left, string? right)
        {
            if (left is null)
            {
                return right is null ? 0 : -1;
            }

            return right is null ? 1 : string.CompareOrdinal(left, right);
        }

        private static int CompareProjectResources(
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
    }
}
