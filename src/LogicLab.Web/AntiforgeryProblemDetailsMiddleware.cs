using Microsoft.AspNetCore.Antiforgery;

namespace LogicLab.Web;

internal sealed class AntiforgeryProblemDetailsMiddleware(
    RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Features.Get<IAntiforgeryValidationFeature>() is
        { IsValid: false }
            ? LogicLabProblemDetails.Create(
                    context,
                    LogicLabProblemDetails.AntiforgeryValidationFailedCode)
                .ExecuteAsync(context)
            : next(context);
    }
}
