using LogicLab.ProjectFormat;

namespace LogicLab.Application.Workspaces;

public sealed class ProjectImportWorkflow
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

    public async Task<WorkspaceOpenOutcome> ImportAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var read = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(source, packagePolicy),
            cancellationToken).ConfigureAwait(false);
        if (read is PackageReadRejected rejectedPackage)
        {
            return new WorkspaceOpenRejected(
                rejectedPackage.Reason,
                [.. rejectedPackage.Diagnostics.Select(item => item.Code)],
                RetryDisposition.DoNotRetry);
        }

        var succeeded = (PackageReadSucceeded)read;
        return await workspace.OpenAsync(
            new ImportProject(succeeded.ImportCandidate),
            cancellationToken).ConfigureAwait(false);
    }
}
