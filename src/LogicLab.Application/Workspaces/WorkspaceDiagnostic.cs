using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;

namespace LogicLab.Application.Workspaces;

/// <summary>Preserves module evidence and its revision across a rejected Workspace operation.</summary>
public abstract record WorkspaceDiagnostic
{
    private protected WorkspaceDiagnostic(ProjectRevisionId? projectRevisionId)
    {
        ProjectRevisionId = projectRevisionId;
    }

    public ProjectRevisionId? ProjectRevisionId { get; }

    public abstract string Code { get; }
}

public sealed record WorkspaceAuthoringDiagnostic(
    ProjectRevisionId? ProjectRevisionId,
    AuthoringDiagnostic Diagnostic) : WorkspaceDiagnostic(ProjectRevisionId)
{
    public override string Code => Diagnostic.Code;
}

public sealed record WorkspaceCompilationDiagnostic(
    ProjectRevisionId ProjectRevisionId,
    CompilerDiagnostic Diagnostic) : WorkspaceDiagnostic(ProjectRevisionId)
{
    public override string Code => Diagnostic.Code;
}

public sealed record WorkspaceSimulationDiagnostic(
    ProjectRevisionId? ProjectRevisionId,
    SimulationDiagnostic Diagnostic) : WorkspaceDiagnostic(ProjectRevisionId)
{
    public override string Code => Diagnostic.Code;
}

public sealed record WorkspacePackageDiagnostic : WorkspaceDiagnostic
{
    public WorkspacePackageDiagnostic(PackageDiagnostic diagnostic) : base((ProjectRevisionId?)null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        // Package records expose an IReadOnlyList, so take ownership at this seam.
        Diagnostic = diagnostic with { Arguments = Array.AsReadOnly(diagnostic.Arguments.ToArray()) };
    }

    public PackageDiagnostic Diagnostic { get; }

    public override string Code => Diagnostic.Code;
}
