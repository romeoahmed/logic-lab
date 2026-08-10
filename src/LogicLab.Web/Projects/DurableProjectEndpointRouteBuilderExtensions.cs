using System.Buffers;
using System.Security.Claims;
using System.Text;
using System.Text.Unicode;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace LogicLab.Web.Projects;

internal static class DurableProjectEndpointRouteBuilderExtensions
{
    internal const string OpenPath = "/projects/open";

    private const string OpenFormMediaType = "application/x-www-form-urlencoded";
    private const int MaximumDurableProjectIdLength = 64;
    private const int MaximumOpenRequestBodyBytes = 4096;

    public static IEndpointConventionBuilder MapDurableProjectEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var openEndpoint = endpoints.MapPost(
                OpenPath,
                async Task<IResult> (
                    HttpContext httpContext,
                    ClaimsPrincipal principal,
                    IEditorWorkspace workspace,
                    CancellationToken cancellationToken) =>
                {
                    var subjectValue = principal.FindFirst(
                        ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(subjectValue))
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.AuthenticationRequiredCode);
                    }

                    if (!HasSupportedFormContentType(httpContext.Request))
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.ProjectOpenRequestInvalidCode);
                    }

                    if (!await HasWellFormedBodyAsync(
                            httpContext.Request,
                            cancellationToken))
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.ProjectOpenRequestInvalidCode);
                    }

                    IFormCollection form;
                    try
                    {
                        form = await httpContext.Request.ReadFormAsync(
                            cancellationToken);
                    }
                    catch (InvalidDataException)
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.ProjectOpenRequestInvalidCode);
                    }

                    var durableProjectId = form.TryGetValue(
                            "durableProjectId",
                            out var durableProjectIds)
                        && durableProjectIds.Count == 1
                            ? durableProjectIds[0]
                            : null;
                    if (string.IsNullOrEmpty(durableProjectId)
                        || durableProjectId.Length > MaximumDurableProjectIdLength)
                    {
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.ProjectOpenRequestInvalidCode);
                    }

                    var outcome = await workspace.OpenAsync(
                        new OpenDurable(
                            new DurableProjectId(durableProjectId),
                            new AuthenticatedWorkspaceCaller(
                                new AuthenticatedSubjectId(subjectValue))),
                        cancellationToken);
                    return outcome switch
                    {
                        WorkspaceOpened opened => Results.LocalRedirect(
                            $"~/editor/{Uri.EscapeDataString(opened.WorkspaceId.Value)}"),
                        WorkspaceOpenRejected rejected =>
                            LogicLabProblemDetails.Create(httpContext, rejected.Code),
                        _ => throw new InvalidOperationException(
                            "The Workspace open outcome hierarchy is closed."),
                    };
                })
            .RequireAuthorization()
            .DisableCookieRedirect()
            .WithMetadata(new RequestSizeLimitAttribute(
                MaximumOpenRequestBodyBytes))
            .WithMetadata(new RequestBodyBufferingMetadata(
                MaximumOpenRequestBodyBytes))
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true))
            .RequireRateLimiting(
                DurableProjectIngressPolicy.OpenRateLimitPolicyName)
            .WithMetadata(new RateLimitProblemDetailsMetadata(
                LogicLabProblemDetails.ProjectOpenRateLimitExceededCode));

        endpoints.MapFallback(OpenPath, IResult (HttpContext httpContext) =>
        {
            httpContext.Response.Headers.Allow = HttpMethods.Post;
            if (HttpMethods.IsHead(httpContext.Request.Method))
            {
                httpContext.Response.StatusCode =
                    StatusCodes.Status405MethodNotAllowed;
                httpContext.Response.ContentType = "application/problem+json";
                return Results.Empty;
            }

            return LogicLabProblemDetails.Create(
                httpContext,
                LogicLabProblemDetails.ProjectOpenMethodNotAllowedCode);
        });

        return openEndpoint;
    }

    private static bool HasSupportedFormContentType(HttpRequest request)
    {
        return MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            && contentType.MediaType.Equals(
                OpenFormMediaType,
                StringComparison.OrdinalIgnoreCase)
            && (!contentType.Charset.HasValue
                || contentType.Encoding?.CodePage == Encoding.UTF8.CodePage);
    }

    private static async Task<bool> HasWellFormedBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Body.CanSeek)
        {
            throw new InvalidOperationException(
                "The Durable Project open request body was not buffered.");
        }

        const int readCapacity = MaximumOpenRequestBodyBytes + 1;
        var buffer = ArrayPool<byte>.Shared.Rent(readCapacity);
        try
        {
            request.Body.Position = 0;
            var length = 0;
            while (length < readCapacity)
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(length, readCapacity - length),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaximumOpenRequestBodyBytes)
            {
                throw new BadHttpRequestException(
                    "The request body is too large.",
                    StatusCodes.Status413PayloadTooLarge);
            }

            return IsWellFormedUrlEncodedUtf8(buffer.AsSpan(0, length));
        }
        finally
        {
            request.Body.Position = 0;
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static bool IsWellFormedUrlEncodedUtf8(Span<byte> body)
    {
        var decodedLength = 0;
        for (var index = 0; index < body.Length; index++)
        {
            var value = body[index];
            if (value == '%')
            {
                if (index + 2 >= body.Length
                    || !TryDecodeHex(body[index + 1], out var high)
                    || !TryDecodeHex(body[index + 2], out var low))
                {
                    return false;
                }

                body[decodedLength++] = (byte)((high << 4) | low);
                index += 2;
                continue;
            }

            body[decodedLength++] = value == '+' ? (byte)' ' : value;
        }

        return Utf8.IsValid(body[..decodedLength]);
    }

    private static bool TryDecodeHex(byte value, out byte decoded)
    {
        decoded = value switch
        {
            >= (byte)'0' and <= (byte)'9' => (byte)(value - '0'),
            >= (byte)'A' and <= (byte)'F' => (byte)(value - 'A' + 10),
            >= (byte)'a' and <= (byte)'f' => (byte)(value - 'a' + 10),
            _ => byte.MaxValue,
        };
        return decoded != byte.MaxValue;
    }
}
