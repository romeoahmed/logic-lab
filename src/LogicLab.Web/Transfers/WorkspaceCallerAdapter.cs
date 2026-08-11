using System.Security.Claims;
using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Transfers;

internal static class WorkspaceCallerAdapter
{
    public static WorkspaceCaller FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var subjectValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(subjectValue)
            ? AnonymousWorkspaceCaller.Instance
            : new AuthenticatedWorkspaceCaller(
                new AuthenticatedSubjectId(subjectValue));
    }
}
