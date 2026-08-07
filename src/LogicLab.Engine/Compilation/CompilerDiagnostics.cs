using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Compilation;

public enum CompilerDiagnosticSeverity
{
    Error,
}

public abstract record CompilerDiagnosticValue
{
    private protected CompilerDiagnosticValue()
    {
    }
}

public sealed record CompilerStableTokenValue(string Value)
    : CompilerDiagnosticValue;

public sealed record CompilerUnsignedDecimalValue(ulong Value)
    : CompilerDiagnosticValue;

public sealed record CompilerDigestValue(string Value)
    : CompilerDiagnosticValue;

public sealed record CompilerCorrelationTokenValue(string Value)
    : CompilerDiagnosticValue;

public sealed record CompilerContractKeyValue(ComponentContractKey Value)
    : CompilerDiagnosticValue;

public sealed record CompilerDiagnosticArgument(
    string Name,
    CompilerDiagnosticValue Value);

public abstract record CompilerSourceLocation
{
    private protected CompilerSourceLocation()
    {
    }
}

public sealed record CompilerProjectRootLocation(ProjectId ProjectId)
    : CompilerSourceLocation;

public sealed record CompilerCircuitLocation(CompilationSource Source)
    : CompilerSourceLocation;

public sealed class CompilerDiagnostic
{
    internal CompilerDiagnostic(
        string code,
        CompilerDiagnosticArgument[] ownedArguments,
        CompilerSourceLocation? primary = null,
        CompilerSourceLocation[]? ownedRelated = null)
    {
        Code = code;
        Severity = CompilerDiagnosticSeverity.Error;
        Arguments = Array.AsReadOnly(ownedArguments);
        Primary = primary;
        Related = ownedRelated is null
            ? []
            : Array.AsReadOnly(ownedRelated);
    }

    public string Code { get; }

    public CompilerDiagnosticSeverity Severity { get; }

    public ReadOnlyCollection<CompilerDiagnosticArgument> Arguments { get; }

    public CompilerSourceLocation? Primary { get; }

    public ReadOnlyCollection<CompilerSourceLocation> Related { get; }
}
