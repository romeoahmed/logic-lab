using System.Collections.ObjectModel;
using LogicLab.ProjectFormat;

namespace LogicLab.Application.Workspaces;

public interface IProjectImportWorkflow
{
    long MaximumCarrierBytes { get; }

    Task<ProjectImportOutcome> ImportAsync(
        Stream source,
        CancellationToken cancellationToken);
}

public abstract record ProjectImportOutcome
{
    private protected ProjectImportOutcome()
    {
    }
}

public sealed record ProjectImported(WorkspaceOpened Workspace)
    : ProjectImportOutcome;

public sealed record ProjectImportRejected : ProjectImportOutcome
{
    public ProjectImportRejected(
        string code,
        IReadOnlyList<string> diagnosticCodes)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }
}

public static class ProjectImportWorkflowFactory
{
    public static IProjectImportWorkflow Create(
        IEditorWorkspace workspace,
        PackagePolicy? packagePolicy = null)
    {
        return new ProjectImportWorkflow(
            workspace,
            packagePolicy ?? PackagePolicy.Development);
    }

    private sealed class ProjectImportWorkflow : IProjectImportWorkflow
    {
        private readonly IEditorWorkspace workspace;
        private readonly PackagePolicy packagePolicy;

        public ProjectImportWorkflow(
            IEditorWorkspace workspace,
            PackagePolicy packagePolicy)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(packagePolicy);
            var maximum = packagePolicy.Limits[(int)PackageDimension.CarrierBytes]
                .Maximum;
            if (maximum > long.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(packagePolicy),
                    "The carrier byte limit must fit the Stream API.");
            }

            this.workspace = workspace;
            this.packagePolicy = packagePolicy;
            MaximumCarrierBytes = checked((long)maximum);
        }

        public long MaximumCarrierBytes { get; }

        public async Task<ProjectImportOutcome> ImportAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            var read = await ProjectPackage.ReadAsync(
                new ProjectPackageReadRequest(source, packagePolicy),
                cancellationToken).ConfigureAwait(false);
            if (read is PackageReadRejected rejectedPackage)
            {
                return new ProjectImportRejected(
                    rejectedPackage.Reason,
                    [.. rejectedPackage.Diagnostics.Select(item => item.Code)]);
            }

            var succeeded = (PackageReadSucceeded)read;
            var opened = await workspace.OpenAsync(
                new ImportProject(succeeded.ImportCandidate),
                cancellationToken).ConfigureAwait(false);
            return opened switch
            {
                WorkspaceOpened imported => new ProjectImported(imported),
                WorkspaceOpenRejected rejectedWorkspace =>
                    new ProjectImportRejected(
                        rejectedWorkspace.Code,
                        rejectedWorkspace.DiagnosticCodes),
                _ => new ProjectImportRejected(
                    WorkspaceOutcomeReasons.WorkspaceInternalDefect,
                    []),
            };
        }
    }
}
