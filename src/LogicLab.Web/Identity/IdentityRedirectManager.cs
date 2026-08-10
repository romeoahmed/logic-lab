using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Identity;

public sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public void RedirectTo(string? returnUrl)
    {
        var localUrl = IsLocal(returnUrl) ? returnUrl! : "/projects";
        navigationManager.NavigateTo(localUrl);
    }

    private static bool IsLocal(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value[0] == '/'
            && (value.Length == 1 || value[1] is not ('/' or '\\'));
    }
}
