using LogicLab.Web.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace LogicLab.Web.Identity;

public static class AccountEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapLogicLabAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(
                "/account/logout",
                async Task<IResult> (SignInManager<ApplicationUser> signInManager) =>
                {
                    await signInManager.SignOutAsync();
                    return Results.LocalRedirect("~/");
                })
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }
}
