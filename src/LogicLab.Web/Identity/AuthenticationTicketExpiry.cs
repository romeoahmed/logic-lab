using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LogicLab.Web.Identity;

internal static class AuthenticationTicketExpiry
{
    internal const string ClaimType = "logiclab:authentication_expires_utc";

    public static Task StampAsync(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var principal = context.Principal
            ?? throw new InvalidOperationException(
                "An application cookie must contain a principal.");
        var expiresUtc = context.Properties.ExpiresUtc
            ?? throw new InvalidOperationException(
                "An application cookie must have an absolute expiry.");
        var identity = principal.Identities.FirstOrDefault(item => item.IsAuthenticated)
            ?? throw new InvalidOperationException(
                "An application cookie must contain an authenticated identity.");
        ReplaceClaim(
            principal,
            identity,
            expiresUtc.ToString("O", CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    public static async Task ValidateAndPreserveAsync(
        CookieValidatePrincipalContext context,
        Func<CookieValidatePrincipalContext, Task> validatePrincipal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(validatePrincipal);
        var timeProvider = context.HttpContext.RequestServices
            .GetRequiredService<TimeProvider>();
        if (context.Principal is not { } originalPrincipal
            || !IsCurrent(originalPrincipal, timeProvider))
        {
            context.RejectPrincipal();
            return;
        }

        var originalExpiry = originalPrincipal.FindFirst(ClaimType)!.Value;

        await validatePrincipal(context);

        if (context.Principal is { } principal
            && principal.Identities.FirstOrDefault(item => item.IsAuthenticated)
                is { } identity)
        {
            ReplaceClaim(principal, identity, originalExpiry);
        }
    }

    public static bool IsCurrent(ClaimsPrincipal principal, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var claims = principal.FindAll(ClaimType).Take(2).ToArray();
        return claims.Length == 1
            && DateTimeOffset.TryParseExact(
                claims[0].Value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresUtc)
            && expiresUtc > timeProvider.GetUtcNow();
    }

    private static void ReplaceClaim(
        ClaimsPrincipal principal,
        ClaimsIdentity identity,
        string expiresUtc)
    {
        foreach (var existing in principal.FindAll(ClaimType).ToArray())
        {
            existing.Subject!.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(
            ClaimType,
            expiresUtc,
            ClaimValueTypes.DateTime));
    }
}
