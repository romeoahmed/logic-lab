using Microsoft.AspNetCore.Antiforgery;

namespace LogicLab.Web;

internal sealed class AntiforgeryProblemDetailsMiddleware(
    RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Features.Get<IAntiforgeryValidationFeature>() is not
            { IsValid: false } failure)
        {
            return next(context);
        }

        // Antiforgery wraps body-read failures, including Kestrel's size rejection.
        var code = failure.Error?.InnerException is BadHttpRequestException
        { StatusCode: StatusCodes.Status413PayloadTooLarge }
            ? LogicLabProblemDetails.RequestBodyTooLargeCode
            : LogicLabProblemDetails.AntiforgeryValidationFailedCode;
        return LogicLabProblemDetails.Create(context, code).ExecuteAsync(context);
    }
}
