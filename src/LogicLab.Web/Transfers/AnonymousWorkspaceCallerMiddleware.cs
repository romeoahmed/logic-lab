using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.DataProtection;

namespace LogicLab.Web.Transfers;

internal sealed class AnonymousWorkspaceCallerMiddleware(
    RequestDelegate next,
    IDataProtectionProvider dataProtectionProvider,
    RateLimiter ingressLimiter)
{
    internal const string CookieName = "__Host-LogicLab.AnonymousCaller";

    private const string PrivateNoStore = "private, no-store";
    private const string ProtectionPurpose =
        "LogicLab.Web.AnonymousWorkspaceCaller.v1";
    private readonly RequestDelegate next =
        next ?? throw new ArgumentNullException(nameof(next));
    private readonly IDataProtector protector =
        (dataProtectionProvider
            ?? throw new ArgumentNullException(nameof(dataProtectionProvider)))
        .CreateProtector(ProtectionPurpose);
    private readonly RateLimiter ingressLimiter =
        ingressLimiter ?? throw new ArgumentNullException(nameof(ingressLimiter));

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var isEditorBootstrap = IsEditorBootstrap(context.GetEndpoint());
        if (isEditorBootstrap)
        {
            context.Response.OnStarting(
                static state =>
                {
                    ((HttpResponse)state).Headers.CacheControl = PrivateNoStore;
                    return Task.CompletedTask;
                },
                context.Response);
        }

        if (context.User.Identities.Any(static identity => identity.IsAuthenticated))
        {
            await next(context);
            return;
        }

        var browserId = ReadBrowserId(context.Request.Cookies[CookieName]);
        if (browserId is null)
        {
            if (!isEditorBootstrap)
            {
                await next(context);
                return;
            }

            using var lease = ingressLimiter.AttemptAcquire();
            if (!lease.IsAcquired)
            {
                if (lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                {
                    context.Response.Headers.RetryAfter = Math.Ceiling(
                            retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
                }

                await LogicLabProblemDetails.Create(
                    context,
                    LogicLabProblemDetails.AnonymousWorkspaceIngressExceededCode)
                    .ExecuteAsync(context);
                return;
            }

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

    private static bool IsEditorBootstrap(Endpoint? endpoint) =>
        endpoint?.Metadata.OfType<ComponentTypeMetadata>()
            .LastOrDefault()?.Type == typeof(Components.Pages.Editor);

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
