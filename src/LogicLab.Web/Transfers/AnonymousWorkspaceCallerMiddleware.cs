using System.Security.Claims;
using System.Security.Cryptography;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.DataProtection;

namespace LogicLab.Web.Transfers;

internal sealed class AnonymousWorkspaceCallerMiddleware(
    RequestDelegate next,
    IDataProtectionProvider dataProtectionProvider)
{
    internal const string CookieName = "__Host-LogicLab.AnonymousCaller";

    private const string ProtectionPurpose =
        "LogicLab.Web.AnonymousWorkspaceCaller.v1";
    private readonly RequestDelegate next =
        next ?? throw new ArgumentNullException(nameof(next));
    private readonly IDataProtector protector =
        (dataProtectionProvider
            ?? throw new ArgumentNullException(nameof(dataProtectionProvider)))
        .CreateProtector(ProtectionPurpose);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.Identities.Any(static identity => identity.IsAuthenticated))
        {
            await next(context);
            return;
        }

        var browserId = ReadBrowserId(context.Request.Cookies[CookieName]);
        if (browserId is null)
        {
            browserId = new AnonymousBrowserId(
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
            context.Response.Cookies.Append(
                CookieName,
                protector.Protect(browserId.Value),
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Path = "/",
                    SameSite = SameSiteMode.Lax,
                    Secure = true,
                });
        }

        context.User.AddIdentity(new ClaimsIdentity(
            [new Claim(
                WorkspaceCallerAdapter.AnonymousBrowserClaimType,
                browserId.Value)]));
        await next(context);
    }

    private AnonymousBrowserId? ReadBrowserId(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            return new AnonymousBrowserId(protector.Unprotect(protectedValue));
        }
        catch (Exception exception) when (exception is CryptographicException
            or ArgumentException)
        {
            return null;
        }
    }
}
