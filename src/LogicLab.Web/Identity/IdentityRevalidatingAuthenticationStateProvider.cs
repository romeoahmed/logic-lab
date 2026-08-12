using System.Security.Claims;
using LogicLab.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Identity;

internal sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options,
    TimeProvider timeProvider)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken) =>
        ValidateSessionAsync(authenticationState, cancellationToken);

    internal async Task<bool> ValidateSessionAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AuthenticationTicketExpiry.IsCurrent(
                authenticationState.User,
                timeProvider))
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(authenticationState.User);
        if (user is null)
        {
            return false;
        }

        if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }

        var principalStamp = authenticationState.User.FindFirst(
            options.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return string.Equals(principalStamp, userStamp, StringComparison.Ordinal);
    }
}
