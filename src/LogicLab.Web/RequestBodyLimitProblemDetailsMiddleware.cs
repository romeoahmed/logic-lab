using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace LogicLab.Web;

internal sealed class RequestBodyLimitProblemDetailsMiddleware(
    RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var maximumBodySize = context.GetEndpoint()?.Metadata
            .GetMetadata<IRequestSizeLimitMetadata>()?
            .MaxRequestBodySize;
        if (maximumBodySize is null)
        {
            await next(context);
            return;
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        var effectiveMaximum = sizeFeature?.MaxRequestBodySize is { } serverMaximum
            ? Math.Min(maximumBodySize.Value, serverMaximum)
            : maximumBodySize.Value;
        if (context.Request.ContentLength > effectiveMaximum)
        {
            await RejectAsync(context);
            return;
        }

        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = effectiveMaximum;
        }

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                && !context.Response.HasStarted)
        {
            context.Response.Clear();
            await RejectAsync(context);
        }
    }

    private static Task RejectAsync(HttpContext context)
        => LogicLabProblemDetails.Create(
                context,
                LogicLabProblemDetails.RequestBodyTooLargeCode)
            .ExecuteAsync(context);
}
