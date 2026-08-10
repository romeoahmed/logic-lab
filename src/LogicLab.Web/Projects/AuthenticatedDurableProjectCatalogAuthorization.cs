using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Projects;

public sealed class AuthenticatedDurableProjectCatalogAuthorization
    : IDurableProjectCatalogAuthorization
{
    public ValueTask<bool> AuthorizeListAsync(
        AuthenticatedSubjectId subjectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }
}
