namespace LogicLab.Web;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "base-uri 'self'; "
        + "connect-src 'self'; "
        + "font-src 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'; "
        + "img-src 'self' data:; "
        + "object-src 'none'; "
        + "script-src 'self'; "
        + "style-src 'self' 'unsafe-inline'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        return next(context);
    }
}
