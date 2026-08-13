using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

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
    internal const string AnonymousWorkspaceIngressExceededCode =
        "anonymous_workspace_ingress_exceeded";
    internal const string AuthenticationRevocationFailedCode =
        "authentication_revocation_failed";
    internal const string AntiforgeryValidationFailedCode =
        "antiforgery_validation_failed";
    internal const string RequestBodyTooLargeCode = "request_body_too_large";
    internal const string CultureRequestInvalidCode =
        CultureEndpointRouteBuilderExtensions.RequestInvalidCode;
    internal const string ForbiddenCode = "forbidden";

    public static IResult Create(HttpContext httpContext, string code)
    {
        return Create(
            httpContext,
            code,
            CurrentCorrelationToken(),
            policyEvidence: null);
    }

    public static IResult Create(
        HttpContext httpContext,
        string code,
        PolicyEvidenceProjection? policyEvidence)
    {
        return Create(httpContext, code, CurrentCorrelationToken(), policyEvidence);
    }

    internal static IResult Create(
        HttpContext httpContext,
        string code,
        string correlationToken)
    {
        return Create(httpContext, code, correlationToken, policyEvidence: null);
    }

    private static IResult Create(
        HttpContext httpContext,
        string code,
        string correlationToken,
        PolicyEvidenceProjection? policyEvidence)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(correlationToken);

        var text = httpContext.RequestServices
            .GetRequiredService<IStringLocalizer<ProblemDetailsText>>();
        var (status, title) = Describe(code, text);
        var problem = new ProblemDetails
        {
            Type = $"{ProblemTypeBase}{code}",
            Title = title,
            Status = status,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = correlationToken;
        if (policyEvidence is not null)
        {
            problem.Extensions["policyId"] = policyEvidence.PolicyId;
            problem.Extensions["policyRevision"] = policyEvidence.PolicyRevision;
            problem.Extensions["dimension"] = policyEvidence.Dimension;
            problem.Extensions["observed"] = policyEvidence.Observed;
        }

        return Results.Problem(problem);
    }

    private static (int Status, string Title) Describe(
        string code,
        IStringLocalizer<ProblemDetailsText> text)
    {
        return code switch
        {
            ProjectOpenRequestInvalidCode => (
                StatusCodes.Status400BadRequest,
                text["ProjectOpenRequest"]),
            ProjectOpenMethodNotAllowedCode => (
                StatusCodes.Status405MethodNotAllowed,
                text["ProjectOpenMethod"]),
            ProjectExportEndpointRouteBuilderExtensions.MethodNotAllowedCode => (
                StatusCodes.Status405MethodNotAllowed,
                text["ExportMethod"]),
            ProjectExportEndpointRouteBuilderExtensions.RateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                text["ExportRateLimited"]),
            AntiforgeryValidationFailedCode => (
                StatusCodes.Status400BadRequest,
                text["AntiforgeryFailed"]),
            RequestBodyTooLargeCode => (
                StatusCodes.Status413PayloadTooLarge,
                text["RequestBodyTooLarge"]),
            CultureRequestInvalidCode => (
                StatusCodes.Status400BadRequest,
                text["CultureRequest"]),
            AuthenticationRequiredCode => (
                StatusCodes.Status401Unauthorized,
                text["AuthenticationRequired"]),
            WorkspaceOutcomeReasons.WorkspaceNotFound or ForbiddenCode =>
                (StatusCodes.Status404NotFound, text["NotFound"]),
            WorkspaceOutcomeReasons.ExportExpired =>
                (StatusCodes.Status404NotFound, text["ExportUnavailable"]),
            DurableProjectCatalogOutcomeReasons.RequestInvalid => (
                StatusCodes.Status422UnprocessableEntity,
                text["ProjectCatalogRequest"]),
            DurableProjectCatalogOutcomeReasons.CursorInvalid => (
                StatusCodes.Status422UnprocessableEntity,
                text["ProjectCatalogCursor"]),
            CompilationOutcomeReasons.Invalid => (
                StatusCodes.Status422UnprocessableEntity,
                text["CompilationInvalid"]),
            CompilationOutcomeReasons.PolicyExhausted => (
                StatusCodes.Status422UnprocessableEntity,
                text["CompilationPolicy"]),
            WorkspaceOutcomeReasons.WorkspaceAdmissionRejected => (
                StatusCodes.Status429TooManyRequests,
                text["WorkspaceAdmission"]),
            ProjectOpenRateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                text["ProjectOpenRateLimited"]),
            AuthenticationRateLimitExceededCode => (
                StatusCodes.Status429TooManyRequests,
                text["AuthenticationRateLimited"]),
            AnonymousWorkspaceIngressExceededCode => (
                StatusCodes.Status429TooManyRequests,
                text["AnonymousIngressUnavailable"]),
            AuthenticationRevocationFailedCode => (
                StatusCodes.Status503ServiceUnavailable,
                text["AuthenticationRevocationFailed"]),
            WorkspaceOutcomeReasons.WorkspaceCancelled => (
                StatusCodes.Status503ServiceUnavailable,
                text["WorkspaceCancelled"]),
            WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure => (
                StatusCodes.Status503ServiceUnavailable,
                text["WorkspaceInfrastructure"]),
            CompilationOutcomeReasons.Cancelled => (
                StatusCodes.Status503ServiceUnavailable,
                text["CompilationCancelled"]),
            CompilationOutcomeReasons.InfrastructureFailure => (
                StatusCodes.Status503ServiceUnavailable,
                text["CompilationInfrastructure"]),
            DurableProjectCatalogOutcomeReasons.Cancelled => (
                StatusCodes.Status503ServiceUnavailable,
                text["ProjectCatalogCancelled"]),
            DurableProjectCatalogOutcomeReasons.InfrastructureFailure => (
                StatusCodes.Status503ServiceUnavailable,
                text["ProjectCatalogInfrastructure"]),
            WorkspaceOutcomeReasons.WorkspaceInternalDefect => (
                StatusCodes.Status500InternalServerError,
                text["WorkspaceInternal"]),
            CompilationOutcomeReasons.InternalDefect => (
                StatusCodes.Status500InternalServerError,
                text["CompilationInternal"]),
            DurableProjectCatalogOutcomeReasons.InternalDefect => (
                StatusCodes.Status500InternalServerError,
                text["ProjectCatalogInternal"]),
            _ => (
                StatusCodes.Status500InternalServerError,
                text["Generic"]),
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
