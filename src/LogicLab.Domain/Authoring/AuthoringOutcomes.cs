using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public enum AuthoringDiagnosticSeverity
{
    Error,
}

public abstract record AuthoringDiagnosticValue
{
    private protected AuthoringDiagnosticValue()
    {
    }
}

public sealed record StableTokenDiagnosticValue(string Value)
    : AuthoringDiagnosticValue;

public sealed record UnsignedDecimalDiagnosticValue(ulong Value)
    : AuthoringDiagnosticValue;

public sealed record ContractKeyDiagnosticValue(ComponentContractKey Value)
    : AuthoringDiagnosticValue;

public sealed record AuthoringDiagnosticArgument(
    string Name,
    AuthoringDiagnosticValue Value);

public sealed class AuthoringDiagnostic
{
    internal AuthoringDiagnostic(
        string code,
        AuthoringDiagnosticArgument[] arguments,
        AuthoredSourceIdentity? primary = null)
    {
        Code = code;
        Severity = AuthoringDiagnosticSeverity.Error;
        Arguments = Array.AsReadOnly(
            (AuthoringDiagnosticArgument[])arguments.Clone());
        Primary = primary;
    }

    public string Code { get; }

    public AuthoringDiagnosticSeverity Severity { get; }

    public ReadOnlyCollection<AuthoringDiagnosticArgument> Arguments { get; }

    public AuthoredSourceIdentity? Primary { get; }
}

public abstract record AuthoredSourceIdentity
{
    private protected AuthoredSourceIdentity()
    {
    }
}

public sealed record ProjectRootSourceIdentity(ProjectId ProjectId)
    : AuthoredSourceIdentity;

public sealed record CircuitRootSourceIdentity(CircuitDefinitionId CircuitDefinitionId)
    : AuthoredSourceIdentity;

public sealed record DefinitionPortSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    DefinitionPortId DefinitionPortId) : AuthoredSourceIdentity;

public sealed record ComponentInstanceSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId) : AuthoredSourceIdentity;

public sealed record InstancePortSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    ComponentInstanceId ComponentInstanceId,
    string PortId) : AuthoredSourceIdentity;

public sealed record NetSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    NetId NetId) : AuthoredSourceIdentity;

public sealed record JunctionSourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    JunctionId JunctionId) : AuthoredSourceIdentity;

public sealed record WireGeometrySourceIdentity(
    CircuitDefinitionId CircuitDefinitionId,
    WireGeometryId WireGeometryId) : AuthoredSourceIdentity;

public abstract record ProjectGenesisOutcome
{
    private protected ProjectGenesisOutcome()
    {
    }
}

public sealed record ProjectGenesisCommitted : ProjectGenesisOutcome
{
    internal ProjectGenesisCommitted(
        ProjectRevision revision,
        AuthoredSourceIdentity[] changedSources)
    {
        Revision = revision;
        ChangedSources = Array.AsReadOnly(
            AuthoringCanonicalizer.Sources(changedSources));
    }

    public ProjectRevision Revision { get; }

    public ReadOnlyCollection<AuthoredSourceIdentity> ChangedSources { get; }

    public ReadOnlyCollection<AuthoredSourceIdentity> RemovedSources { get; } =
        ReadOnlyCollection<AuthoredSourceIdentity>.Empty;

    public ReadOnlyCollection<AuthoringDiagnostic> Diagnostics { get; } =
        ReadOnlyCollection<AuthoringDiagnostic>.Empty;
}

public sealed record ProjectGenesisRejected : ProjectGenesisOutcome
{
    internal ProjectGenesisRejected(AuthoringDiagnostic[] diagnostics)
    {
        Diagnostics = Array.AsReadOnly(
            AuthoringCanonicalizer.Diagnostics(diagnostics));
    }

    public ReadOnlyCollection<AuthoringDiagnostic> Diagnostics { get; }

    public string Reason { get; } = "authoring_invalid";
}

public abstract record EditOutcome
{
    private protected EditOutcome()
    {
    }
}

public sealed record EditCommitted : EditOutcome
{
    internal EditCommitted(
        ProjectRevision revision,
        AuthoredSourceIdentity[] changedSources,
        AuthoredSourceIdentity[] removedSources)
    {
        Revision = revision;
        ChangedSources = Array.AsReadOnly(
            AuthoringCanonicalizer.Sources(changedSources));
        RemovedSources = Array.AsReadOnly(
            AuthoringCanonicalizer.Sources(removedSources));
    }

    public ProjectRevision Revision { get; }

    public ReadOnlyCollection<AuthoredSourceIdentity> ChangedSources { get; }

    public ReadOnlyCollection<AuthoredSourceIdentity> RemovedSources { get; }

    public ReadOnlyCollection<AuthoringDiagnostic> Diagnostics { get; } =
        ReadOnlyCollection<AuthoringDiagnostic>.Empty;
}

public sealed record EditRejected : EditOutcome
{
    internal EditRejected(AuthoringDiagnostic[] diagnostics)
    {
        Diagnostics = Array.AsReadOnly(
            AuthoringCanonicalizer.Diagnostics(diagnostics));
    }

    public ReadOnlyCollection<AuthoringDiagnostic> Diagnostics { get; }

    public string Reason { get; } = "authoring_invalid";
}
