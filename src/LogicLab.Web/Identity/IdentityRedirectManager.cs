using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LogicLab.Web.Identity;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public void RedirectTo(string? returnUrl)
    {
        var localUrl = returnUrl?.StartsWith('/') == true
            && RedirectHttpResult.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/projects";
        navigationManager.NavigateTo(localUrl);
    }
}
