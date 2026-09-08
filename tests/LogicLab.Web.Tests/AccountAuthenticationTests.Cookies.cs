using System.Security.Claims;
using LogicLab.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

internal sealed partial class AccountAuthenticationTests
{
    [Test]
    public async Task ApplicationCookie_SigningIn_EmbedsAbsoluteExpiryClaim()
    {
        var expiresUtc = Now.AddMinutes(5);
        using var host = CreateIdentityHost();
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "cookie-user")],
            IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = expiresUtc,
        };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningInContext(
            new DefaultHttpContext { RequestServices = host.Services },
            scheme,
            options,
            principal,
            properties,
            new CookieOptions());

        await options.Events.SigningIn(context);
        var expiryClaims = context.Principal!
            .FindAll(AuthenticationExpiryClaimType)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(expiryClaims).Count().IsEqualTo(1);
            await Assert.That(expiryClaims.Single().Value)
                .IsEqualTo(expiresUtc.ToString("O"));
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task ApplicationCookie_SecurityStampRefresh_PreservesAbsoluteExpiryClaim()
    {
        var expiresUtc = Now.AddMinutes(5);
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        await using var scope = host.Services.CreateAsyncScope();
        var (user, stamp, identityOptions) = await CreateUserAsync(scope.ServiceProvider);
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var principal = AuthenticationStateFor(
            user,
            stamp,
            identityOptions,
            expiresUtc.ToString("O")).User;
        var properties = new AuthenticationProperties
        {
            IssuedUtc = Now.AddMinutes(-5),
            ExpiresUtc = expiresUtc,
        };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            scheme,
            options,
            new AuthenticationTicket(
                principal,
                properties,
                IdentityConstants.ApplicationScheme));

        await options.Events.ValidatePrincipal(context);
        var expiryClaims = context.Principal!
            .FindAll(AuthenticationExpiryClaimType)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(context.ShouldRenew).IsTrue();
            await Assert.That(expiryClaims).Count().IsEqualTo(1);
            await Assert.That(expiryClaims.Single().Value)
                .IsEqualTo(expiresUtc.ToString("O"));
        }
    }

    [Test]
    public async Task ApplicationCookie_InvalidAbsoluteExpiry_RejectsHttpPrincipal()
    {
        using var host = CreateIdentityHost();
        var identityOptions = host.Services
            .GetRequiredService<IOptions<IdentityOptions>>();
        var user = new ApplicationUser { Id = "expiry-user" };
        const string stamp = "expiry-stamp";
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        string?[] invalidExpiries =
        [
            null,
            "not-a-date",
            Now.ToString("O"),
            Now.AddSeconds(-1).ToString("O"),
        ];

        foreach (var invalidExpiry in invalidExpiries)
        {
            var principal = AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                invalidExpiry).User;
            var context = new CookieValidatePrincipalContext(
                new DefaultHttpContext { RequestServices = host.Services },
                scheme,
                options,
                new AuthenticationTicket(
                    principal,
                    new AuthenticationProperties
                    {
                        IssuedUtc = Now.AddMinutes(-5),
                        ExpiresUtc = Now.AddMinutes(5),
                    },
                    IdentityConstants.ApplicationScheme));

            await options.Events.ValidatePrincipal(context);

            await Assert.That(context.Principal).IsNull();
        }
    }
}
