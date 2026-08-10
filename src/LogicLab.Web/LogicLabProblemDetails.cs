using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LogicLab.Web;

internal static class LogicLabProblemDetails
{
    private const string ProblemTypeBase = "https://logiclab.example/problems/";

    internal const string ProjectOpenRequestInvalidCode =
        "project_open_request_invalid";

    public static IResult Create(HttpContext httpContext, string code)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(code);

        var status = StatusFor(code);
        var problem = new ProblemDetails
        {
            Type = $"{ProblemTypeBase}{code}",
            Title = TitleFor(code),
            Status = status,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = CorrelationToken();
        return new LogicLabProblemResult(problem);
    }

    private static int StatusFor(string code)
    {
        return code switch
        {
            ProjectOpenRequestInvalidCode => StatusCodes.Status400BadRequest,
            "authentication_required" => StatusCodes.Status401Unauthorized,
            "workspace_not_found" or "forbidden" => StatusCodes.Status404NotFound,
            "project_catalog_request_invalid" or "project_catalog_cursor_invalid" =>
                StatusCodes.Status422UnprocessableEntity,
            "workspace_admission_rejected" => StatusCodes.Status429TooManyRequests,
            "workspace_cancelled" or "workspace_infrastructure_failure"
                or "project_catalog_cancelled"
                or "project_catalog_infrastructure_failure" =>
                StatusCodes.Status503ServiceUnavailable,
            "workspace_internal_defect" or "project_catalog_internal_defect" =>
                StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError,
        };
    }

    private static string TitleFor(string code)
    {
        return code switch
        {
            ProjectOpenRequestInvalidCode => "The project open request is invalid",
            "authentication_required" => "Authentication is required",
            "workspace_not_found" or "forbidden" => "The requested resource was not found",
            "project_catalog_request_invalid" => "The project catalog request is invalid",
            "project_catalog_cursor_invalid" => "The project catalog cursor is invalid",
            "workspace_admission_rejected" => "Workspace capacity is unavailable",
            "workspace_cancelled" => "Workspace opening was cancelled",
            "workspace_infrastructure_failure" => "The Workspace service is unavailable",
            "project_catalog_cancelled" => "Project catalog loading was cancelled",
            "project_catalog_infrastructure_failure" =>
                "The project catalog is unavailable",
            "workspace_internal_defect" => "The Workspace could not be opened",
            "project_catalog_internal_defect" =>
                "The project catalog could not be loaded",
            _ => "The request could not be completed",
        };
    }

    private static string CorrelationToken()
    {
        return Activity.Current is { TraceId: var traceId } && traceId != default
            ? traceId.ToHexString()
            : Guid.CreateVersion7().ToString("N");
    }

    private sealed class LogicLabProblemResult(ProblemDetails problem) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = problem.Status
                ?? StatusCodes.Status500InternalServerError;
            var service = httpContext.RequestServices
                .GetRequiredService<IProblemDetailsService>();
            return service.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
            }).AsTask();
        }
    }
}

internal sealed class LogicLabProblemDetailsWriter(
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ProblemDetails.Extensions.ContainsKey("code");
    }

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = context.HttpContext.Response;
        response.StatusCode = context.ProblemDetails.Status
            ?? StatusCodes.Status500InternalServerError;
        return new ValueTask(response.WriteAsJsonAsync(
            context.ProblemDetails,
            jsonOptions.Value.SerializerOptions,
            "application/problem+json",
            context.HttpContext.RequestAborted));
    }
}
