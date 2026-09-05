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
        // Error adapters may clear a response before replacing it with Problem Details.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpResponse)state).Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
            headers["Referrer-Policy"] = "no-referrer";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            return Task.CompletedTask;
        }, context.Response);
        return next(context);
    }
}
