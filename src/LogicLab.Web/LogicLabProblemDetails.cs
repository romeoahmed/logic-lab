using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LogicLab.Web;

internal static class LogicLabProblemDetails
{
    private const string ProblemTypeBase = "https://logiclab.example/problems/";

    internal const string ProjectOpenRequestInvalidCode =
        "project_open_request_invalid";
    internal const string ProjectOpenMethodNotAllowedCode =
        "project_open_method_not_allowed";
    internal const string ProjectOpenRateLimitExceededCode =
        "project_open_rate_limit_exceeded";
    internal const string AuthenticationRequiredCode =
        "authentication_required";
    internal const string AuthenticationRateLimitExceededCode =
        "authentication_rate_limit_exceeded";
    internal const string AntiforgeryValidationFailedCode =
        "antiforgery_validation_failed";
    internal const string RequestBodyTooLargeCode = "request_body_too_large";
    internal const string ForbiddenCode = "forbidden";

    public static IResult Create(HttpContext httpContext, string code)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(code);

        var (status, title) = Describe(code);
        var problem = new ProblemDetails
        {
            Type = $"{ProblemTypeBase}{code}",
            Title = title,
            Status = status,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = CorrelationToken();
        return Results.Problem(problem);
    }

    private static (int Status, string Title) Describe(string code)
    {
        return code switch
        {
            ProjectOpenRequestInvalidCode => (
                StatusCodes.Status400BadRequest,
                "The project open request is invalid"),
            ProjectOpenMethodNotAllowedCode => (
                StatusCodes.Status405MethodNotAllowed,
                "The request method is not supported for project opening"),
            AntiforgeryValidationFailedCode => (
                StatusCodes.Status400BadRequest,
                "Antiforgery validation failed"),
            RequestBodyTooLargeCode => (
                StatusCodes.Status413PayloadTooLarge,
                "The request body is too large"),
            AuthenticationRequiredCode => (
                StatusCodes.Status401Unauthorized,
                "Authentication is required"),
            "workspace_not_found" or ForbiddenCode =>
                (StatusCodes.Status404NotFound, "The requested resource was not found"),
            "project_catalog_request_invalid" => (
                StatusCodes.Status422UnprocessableEntity,
                "The project catalog request is invalid"),
            "project_catalog_cursor_invalid" => (
                StatusCodes.Status422UnprocessableEntity,
                "The project catalog cursor is invalid"),
            "compilation_invalid" => (
                StatusCodes.Status422UnprocessableEntity,
                "The project revision is invalid"),
            "compilation_policy_exhausted" => (
                StatusCodes.Status422UnprocessableEntity,
                "The project exceeds compilation policy"),
            "workspace_admission_rejected" => (
                StatusCodes.Status429TooManyRequests,
                "Workspace capacity is unavailable"),
            ProjectOpenRateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                "Too many project open requests"),
            AuthenticationRateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                "Too many authentication requests"),
            "workspace_cancelled" => (
                StatusCodes.Status503ServiceUnavailable,
                "Workspace opening was cancelled"),
            "workspace_infrastructure_failure" => (
                StatusCodes.Status503ServiceUnavailable,
                "The Workspace service is unavailable"),
            "compilation_cancelled" => (
                StatusCodes.Status503ServiceUnavailable,
                "Project compilation was cancelled"),
            "compilation_infrastructure_failure" => (
                StatusCodes.Status503ServiceUnavailable,
                "The Compiler service is unavailable"),
            "project_catalog_cancelled" => (
                StatusCodes.Status503ServiceUnavailable,
                "Project catalog loading was cancelled"),
            "project_catalog_infrastructure_failure" => (
                StatusCodes.Status503ServiceUnavailable,
                "The project catalog is unavailable"),
            "workspace_internal_defect" => (
                StatusCodes.Status500InternalServerError,
                "The Workspace could not be opened"),
            "compilation_internal_defect" => (
                StatusCodes.Status500InternalServerError,
                "The project could not be compiled"),
            "project_catalog_internal_defect" => (
                StatusCodes.Status500InternalServerError,
                "The project catalog could not be loaded"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "The request could not be completed"),
        };
    }

    private static string CorrelationToken()
    {
        return Activity.Current is { TraceId: var traceId } && traceId != default
            ? traceId.ToHexString()
            : Guid.CreateVersion7().ToString("N");
    }
}

internal sealed record RateLimitProblemDetailsMetadata
{
    public RateLimitProblemDetailsMetadata(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
    }

    public string Code { get; }
}
