using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

internal static class CompilerCanonicalizer
{
    public static CompilerDiagnostic[] Diagnostics(
        IEnumerable<CompilerDiagnostic> diagnostics)
    {
        return [.. new SortedSet<CompilerDiagnostic>(
            diagnostics,
            CompilerDiagnosticComparer.Instance)];
    }

    private sealed class CompilerDiagnosticComparer : IComparer<CompilerDiagnostic>
    {
        public static CompilerDiagnosticComparer Instance { get; } = new();

        public int Compare(CompilerDiagnostic? left, CompilerDiagnostic? right)
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

            var phaseComparison = Phase(left.Code).CompareTo(Phase(right.Code));
            if (phaseComparison != 0)
            {
                return phaseComparison;
            }

            var primaryComparison = CompareLocations(left.Primary, right.Primary);
            if (primaryComparison != 0)
            {
                return primaryComparison;
            }

            var codeComparison = string.CompareOrdinal(left.Code, right.Code);
            if (codeComparison != 0)
            {
                return codeComparison;
            }

            var argumentsComparison = CompareArguments(left.Arguments, right.Arguments);
            return argumentsComparison != 0
                ? argumentsComparison
                : CompareLocationCollections(left.Related, right.Related);
        }

        private static int CompareArguments(
            ReadOnlyCollection<CompilerDiagnosticArgument> left,
            ReadOnlyCollection<CompilerDiagnosticArgument> right)
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
            CompilerDiagnosticValue left,
            CompilerDiagnosticValue right)
        {
            var kindComparison = ValueKind(left).CompareTo(ValueKind(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (CompilerStableTokenValue l, CompilerStableTokenValue r) =>
                    string.CompareOrdinal(l.Value, r.Value),
                (CompilerUnsignedDecimalValue l, CompilerUnsignedDecimalValue r) =>
                    l.Value.CompareTo(r.Value),
                (CompilerDigestValue l, CompilerDigestValue r) =>
                    string.CompareOrdinal(l.Value, r.Value),
                (CompilerCorrelationTokenValue l, CompilerCorrelationTokenValue r) =>
                    string.CompareOrdinal(l.Value, r.Value),
                (CompilerContractKeyValue l, CompilerContractKeyValue r) =>
                    CompareContractKeys(l.Value, r.Value),
                _ => throw new InvalidOperationException(
                    "The Compiler Diagnostic Value variant is undefined."),
            };
        }

        private static int CompareLocationCollections(
            ReadOnlyCollection<CompilerSourceLocation> left,
            ReadOnlyCollection<CompilerSourceLocation> right)
        {
            var commonCount = Math.Min(left.Count, right.Count);
            for (var index = 0; index < commonCount; index++)
            {
                var comparison = CompareLocations(left[index], right[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private static int CompareLocations(
            CompilerSourceLocation? left,
            CompilerSourceLocation? right)
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

            var kindComparison = LocationKind(left).CompareTo(LocationKind(right));
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return (left, right) switch
            {
                (CompilerProjectRootLocation l, CompilerProjectRootLocation r) =>
                    string.CompareOrdinal(l.ProjectId.Value, r.ProjectId.Value),
                (CompilerCircuitLocation l, CompilerCircuitLocation r) =>
                    CompilationSourceComparer.Instance.Compare(l.Source, r.Source),
                _ => throw new InvalidOperationException(
                    "The Compiler Source Location variant is undefined."),
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

        private static int Phase(string code)
        {
            return code switch
            {
                "compiler_entry_definition_missing" or
                    "compiler_library_version_mismatch" or
                    "compiler_library_digest_mismatch" => 0,
                "compiler_contract_unresolved" or
                    "compiler_hierarchy_recursion" or
                    "compiler_parameter_schema_mismatch" or
                    "compiler_port_unresolved" => 1,
                "compiler_required_terminal_unconnected" or
                    "compiler_width_mismatch" or
                    "compiler_illegal_port_direction" => 2,
                "compiler_policy_exhausted" => 3,
                "compiler_internal_invariant" => 4,
                _ => throw new InvalidOperationException(
                    "The Compiler Diagnostic code is undefined."),
            };
        }

        private static int ValueKind(CompilerDiagnosticValue value)
        {
            return value switch
            {
                CompilerStableTokenValue => 0,
                CompilerUnsignedDecimalValue => 1,
                CompilerDigestValue => 2,
                CompilerCorrelationTokenValue => 3,
                CompilerContractKeyValue => 4,
                _ => throw new InvalidOperationException(
                    "The Compiler Diagnostic Value variant is undefined."),
            };
        }

        private static int LocationKind(CompilerSourceLocation location)
        {
            return location switch
            {
                CompilerProjectRootLocation => 0,
                CompilerCircuitLocation => 1,
                _ => throw new InvalidOperationException(
                    "The Compiler Source Location variant is undefined."),
            };
        }
    }
}
