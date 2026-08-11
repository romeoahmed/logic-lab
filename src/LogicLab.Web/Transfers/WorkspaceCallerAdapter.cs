using System.Security.Claims;
using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Transfers;

internal static class WorkspaceCallerAdapter
{
    internal const string AnonymousBrowserClaimType =
        "https://logiclab.example/claims/anonymous-browser-id";

    public static WorkspaceCaller? FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var authenticatedIdentities = principal.Identities
            .Where(static identity => identity.IsAuthenticated)
            .ToArray();
        var subjectValue = authenticatedIdentities
            .Select(static identity =>
                identity.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            .FirstOrDefault(static value => !string.IsNullOrEmpty(value));
        if (!string.IsNullOrEmpty(subjectValue))
        {
            return new AuthenticatedWorkspaceCaller(
                new AuthenticatedSubjectId(subjectValue));
        }

        if (authenticatedIdentities.Length != 0)
        {
            return null;
        }

        var browserValue = principal.FindFirst(AnonymousBrowserClaimType)?.Value;
        return string.IsNullOrEmpty(browserValue)
            ? AnonymousWorkspaceCaller.Instance
            : new AnonymousBrowserWorkspaceCaller(
                new AnonymousBrowserId(browserValue));
    }

    public static WorkspaceCaller? FromTransferPrincipal(
        ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var caller = FromPrincipal(principal);
        return caller switch
        {
            AnonymousBrowserWorkspaceCaller => caller,
            AuthenticatedWorkspaceCaller => caller,
            _ => null,
        };
    }
}
