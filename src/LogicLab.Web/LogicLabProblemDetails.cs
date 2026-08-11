using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;
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
        WorkspaceOutcomeReasons.AuthenticationRequired;
    internal const string AuthenticationRateLimitExceededCode =
        "authentication_rate_limit_exceeded";
    internal const string AuthenticationRevocationFailedCode =
        "authentication_revocation_failed";
    internal const string AntiforgeryValidationFailedCode =
        "antiforgery_validation_failed";
    internal const string RequestBodyTooLargeCode = "request_body_too_large";
    internal const string ForbiddenCode = "forbidden";

    public static IResult Create(HttpContext httpContext, string code)
    {
        return Create(httpContext, code, CurrentCorrelationToken());
    }

    internal static IResult Create(
        HttpContext httpContext,
        string code,
        string correlationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(correlationToken);

        var (status, title) = Describe(code);
        var problem = new ProblemDetails
        {
            Type = $"{ProblemTypeBase}{code}",
            Title = title,
            Status = status,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = correlationToken;
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
            WorkspaceOutcomeReasons.WorkspaceNotFound or ForbiddenCode =>
                (StatusCodes.Status404NotFound, "The requested resource was not found"),
            DurableProjectCatalogOutcomeReasons.RequestInvalid => (
                StatusCodes.Status422UnprocessableEntity,
                "The project catalog request is invalid"),
            DurableProjectCatalogOutcomeReasons.CursorInvalid => (
                StatusCodes.Status422UnprocessableEntity,
                "The project catalog cursor is invalid"),
            CompilationOutcomeReasons.Invalid => (
                StatusCodes.Status422UnprocessableEntity,
                "The project revision is invalid"),
            CompilationOutcomeReasons.PolicyExhausted => (
                StatusCodes.Status422UnprocessableEntity,
                "The project exceeds compilation policy"),
            WorkspaceOutcomeReasons.WorkspaceAdmissionRejected => (
                StatusCodes.Status429TooManyRequests,
                "Workspace capacity is unavailable"),
            ProjectOpenRateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                "Too many project open requests"),
            AuthenticationRateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                "Too many authentication requests"),
            AuthenticationRevocationFailedCode => (
                StatusCodes.Status503ServiceUnavailable,
                "The authentication session could not be revoked"),
            WorkspaceOutcomeReasons.WorkspaceCancelled => (
                StatusCodes.Status503ServiceUnavailable,
                "Workspace opening was cancelled"),
            WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure => (
                StatusCodes.Status503ServiceUnavailable,
                "The Workspace service is unavailable"),
            CompilationOutcomeReasons.Cancelled => (
                StatusCodes.Status503ServiceUnavailable,
                "Project compilation was cancelled"),
            CompilationOutcomeReasons.InfrastructureFailure => (
                StatusCodes.Status503ServiceUnavailable,
                "The Compiler service is unavailable"),
            DurableProjectCatalogOutcomeReasons.Cancelled => (
                StatusCodes.Status503ServiceUnavailable,
                "Project catalog loading was cancelled"),
            DurableProjectCatalogOutcomeReasons.InfrastructureFailure => (
                StatusCodes.Status503ServiceUnavailable,
                "The project catalog is unavailable"),
            WorkspaceOutcomeReasons.WorkspaceInternalDefect => (
                StatusCodes.Status500InternalServerError,
                "The Workspace could not be opened"),
            CompilationOutcomeReasons.InternalDefect => (
                StatusCodes.Status500InternalServerError,
                "The project could not be compiled"),
            DurableProjectCatalogOutcomeReasons.InternalDefect => (
                StatusCodes.Status500InternalServerError,
                "The project catalog could not be loaded"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "The request could not be completed"),
        };
    }

    internal static string CurrentCorrelationToken()
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
