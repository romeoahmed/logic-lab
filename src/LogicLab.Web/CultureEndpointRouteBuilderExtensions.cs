using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace LogicLab.Web;

internal static class CultureEndpointRouteBuilderExtensions
{
    internal const string Path = "/culture";
    internal const string RequestInvalidCode = "culture_request_invalid";

    private const int MaximumRequestBodyBytes = 1024;
    public static IEndpointConventionBuilder MapLogicLabCultureEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapPost(Path, HandleAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBodyBytes))
            .WithMetadata(new RequestBodyBufferingMetadata(MaximumRequestBodyBytes))
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!HasSupportedFormContentType(httpContext.Request))
        {
            return Invalid(httpContext);
        }

        IFormCollection form;
        try
        {
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Invalid(httpContext);
        }

        var culture = SingleValue(form, "culture");
        var returnUrl = SingleValue(form, "returnUrl");
        if (culture is null
            || !LogicLabCultures.IsSupported(culture)
            || returnUrl is null
            || !RedirectHttpResult.IsLocalUrl(returnUrl))
        {
            return Invalid(httpContext);
        }

        httpContext.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture!)),
            new CookieOptions
            {
                Expires = timeProvider.GetUtcNow().AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Secure = true,
            });
        return Results.LocalRedirect(returnUrl!);
    }

    private static string? SingleValue(IFormCollection form, string name)
    {
        return form.TryGetValue(name, out var values) && values.Count == 1
            ? values[0]
            : null;
    }

    private static bool HasSupportedFormContentType(HttpRequest request)
    {
        return MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            && contentType.MediaType.Equals(
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase)
            && (!contentType.Charset.HasValue
                || contentType.Encoding?.CodePage == Encoding.UTF8.CodePage);
    }

    private static IResult Invalid(HttpContext httpContext)
    {
        return LogicLabProblemDetails.Create(httpContext, RequestInvalidCode);
    }
}
