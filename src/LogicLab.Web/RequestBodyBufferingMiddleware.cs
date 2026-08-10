namespace LogicLab.Web;

internal sealed record RequestBodyBufferingMetadata(int MemoryThresholdBytes);

internal sealed class RequestBodyBufferingMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint()?.Metadata
            .GetMetadata<RequestBodyBufferingMetadata>() is { } metadata)
        {
            context.Request.EnableBuffering(metadata.MemoryThresholdBytes);
        }

        return next(context);
    }
}
