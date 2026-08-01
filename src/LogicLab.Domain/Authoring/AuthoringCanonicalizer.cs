using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

internal static class AuthoringCanonicalizer
{
    public static AuthoredSourceIdentity[] Sources(
        IEnumerable<AuthoredSourceIdentity> sources)
    {
        return sources
            .Distinct()
            .OrderBy(source => source, AuthoredSourceIdentityComparer.Instance)
            .ToArray();
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

        return canonical.ToArray();
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

            var kindComparison = KindOrder(left).CompareTo(KindOrder(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (ProjectRootSourceIdentity l, ProjectRootSourceIdentity r) =>
                    string.CompareOrdinal(l.ProjectId.Value, r.ProjectId.Value),
                (CircuitRootSourceIdentity l, CircuitRootSourceIdentity r) =>
                    string.CompareOrdinal(
                        l.CircuitDefinitionId.Value,
                        r.CircuitDefinitionId.Value),
                (ComponentInstanceSourceIdentity l, ComponentInstanceSourceIdentity r) =>
                    CompareCircuitThenEntity(
                        l.CircuitDefinitionId.Value,
                        l.ComponentInstanceId.Value,
                        r.CircuitDefinitionId.Value,
                        r.ComponentInstanceId.Value),
                (InstancePortSourceIdentity l, InstancePortSourceIdentity r) =>
                    CompareInstancePorts(l, r),
                (NetSourceIdentity l, NetSourceIdentity r) =>
                    CompareCircuitThenEntity(
                        l.CircuitDefinitionId.Value,
                        l.NetId.Value,
                        r.CircuitDefinitionId.Value,
                        r.NetId.Value),
                _ => throw new InvalidOperationException(
                    "The Authored Source Identity variant is undefined."),
            };
        }

        private static int KindOrder(AuthoredSourceIdentity identity)
        {
            return identity switch
            {
                ProjectRootSourceIdentity => 0,
                CircuitRootSourceIdentity => 1,
                ComponentInstanceSourceIdentity => 2,
                InstancePortSourceIdentity => 3,
                NetSourceIdentity => 4,
                _ => throw new InvalidOperationException(
                    "The Authored Source Identity variant is undefined."),
            };
        }

        private static int CompareCircuitThenEntity(
            string leftCircuit,
            string leftEntity,
            string rightCircuit,
            string rightEntity)
        {
            var circuitComparison = string.CompareOrdinal(leftCircuit, rightCircuit);
            return circuitComparison != 0
                ? circuitComparison
                : string.CompareOrdinal(leftEntity, rightEntity);
        }

        private static int CompareInstancePorts(
            InstancePortSourceIdentity left,
            InstancePortSourceIdentity right)
        {
            var entityComparison = CompareCircuitThenEntity(
                left.CircuitDefinitionId.Value,
                left.ComponentInstanceId.Value,
                right.CircuitDefinitionId.Value,
                right.ComponentInstanceId.Value);
            return entityComparison != 0
                ? entityComparison
                : string.CompareOrdinal(left.PortId, right.PortId);
        }
    }
}
