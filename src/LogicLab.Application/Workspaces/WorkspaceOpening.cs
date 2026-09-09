using System.Collections.ObjectModel;
using LogicLab.Application.Examples;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

public abstract record OpenWorkspaceRequest
{
    private protected OpenWorkspaceRequest(WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        Caller = caller;
    }

    public WorkspaceCaller Caller { get; }
}

public sealed record CreateSandbox : OpenWorkspaceRequest
{
    public CreateSandbox(
        string projectDisplayName,
        string entryCircuitDefinitionDisplayName,
        WorkspaceCaller caller)
        : base(caller)
    {
        ArgumentNullException.ThrowIfNull(projectDisplayName);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionDisplayName);
        ProjectDisplayName = projectDisplayName;
        EntryCircuitDefinitionDisplayName = entryCircuitDefinitionDisplayName;
    }

    public string ProjectDisplayName { get; }

    public string EntryCircuitDefinitionDisplayName { get; }
}

public sealed record OpenExample : OpenWorkspaceRequest
{
    public OpenExample(ExampleProject example, WorkspaceCaller caller)
        : base(caller)
    {
        if (!Enum.IsDefined(example))
        {
            throw new ArgumentOutOfRangeException(nameof(example));
        }

        Example = example;
    }

    public ExampleProject Example { get; }
}

public sealed record OpenDurable : OpenWorkspaceRequest
{
    public OpenDurable(
        DurableProjectId durableProjectId,
        WorkspaceCaller caller)
        : base(caller)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        DurableProjectId = durableProjectId;
    }

    public DurableProjectId DurableProjectId { get; }
}

public sealed record ImportProject : OpenWorkspaceRequest
{
    public ImportProject(
        ProjectImportCandidate importCandidate,
        WorkspaceCaller caller)
        : base(caller)
    {
        ArgumentNullException.ThrowIfNull(importCandidate);
        ImportCandidate = importCandidate;
    }

    public ProjectImportCandidate ImportCandidate { get; }
}

public abstract record WorkspaceOpenOutcome
{
    private protected WorkspaceOpenOutcome()
    {
    }
}

public sealed record WorkspaceOpened(
    WorkspaceId WorkspaceId,
    WorkspaceProjection Projection) : WorkspaceOpenOutcome;

public sealed record WorkspaceOpenRejected : WorkspaceOpenOutcome
{
    public WorkspaceOpenRejected(
        string code,
        IReadOnlyList<WorkspaceDiagnostic> diagnostics,
        RetryDisposition retryDisposition,
        PolicyEvidenceProjection? policyEvidence = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Code = code;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        DiagnosticCodes = Array.AsReadOnly(Diagnostics.Select(item => item.Code).ToArray());
        RetryDisposition = retryDisposition;
        PolicyEvidence = policyEvidence;
    }

    public string Code { get; }

    public ReadOnlyCollection<WorkspaceDiagnostic> Diagnostics { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }

    public PolicyEvidenceProjection? PolicyEvidence { get; }
}
