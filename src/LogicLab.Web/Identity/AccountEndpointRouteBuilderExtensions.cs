using System.Security.Claims;
using LogicLab.Web.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace LogicLab.Web.Identity;

internal static class AccountEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapLogicLabAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(
                "/account/logout",
                async Task<IResult> (
                    ClaimsPrincipal principal,
                    SignInManager<ApplicationUser> signInManager) =>
                {
                    var user = await signInManager.UserManager.GetUserAsync(principal);
                    if (user is not null)
                    {
                        var result = await signInManager.UserManager
                            .UpdateSecurityStampAsync(user);
                        if (!result.Succeeded)
                        {
                            throw new InvalidOperationException(
                                "The authenticated session could not be revoked.");
                        }
                    }

                    await signInManager.SignOutAsync();
                    return Results.LocalRedirect("~/");
                })
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }
}
