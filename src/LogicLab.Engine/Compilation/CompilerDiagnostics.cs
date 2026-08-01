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
    private readonly CompilerDiagnosticArgument[] arguments;
    private readonly CompilerSourceLocation[] related;

    internal CompilerDiagnostic(
        string code,
        CompilerDiagnosticArgument[] arguments,
        CompilerSourceLocation? primary = null,
        CompilerSourceLocation[]? related = null)
    {
        Code = code;
        Severity = CompilerDiagnosticSeverity.Error;
        this.arguments = (CompilerDiagnosticArgument[])arguments.Clone();
        this.related = related is null ? [] : (CompilerSourceLocation[])related.Clone();
        Arguments = Array.AsReadOnly(this.arguments);
        Primary = primary;
        Related = Array.AsReadOnly(this.related);
    }

    public string Code { get; }

    public CompilerDiagnosticSeverity Severity { get; }

    public ReadOnlyCollection<CompilerDiagnosticArgument> Arguments { get; }

    public CompilerSourceLocation? Primary { get; }

    public ReadOnlyCollection<CompilerSourceLocation> Related { get; }
}
