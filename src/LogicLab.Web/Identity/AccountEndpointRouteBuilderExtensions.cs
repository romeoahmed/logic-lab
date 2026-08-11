using System.Security.Claims;
using LogicLab.Web.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LogicLab.Web.Identity;

internal static partial class AccountEndpointRouteBuilderExtensions
{
    private const string LogCategory =
        "LogicLab.Web.Identity.AccountEndpointRouteBuilderExtensions";

    public static IEndpointConventionBuilder MapLogicLabAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(
                "/account/logout",
                async Task<IResult> (
                    HttpContext httpContext,
                    ClaimsPrincipal principal,
                    SignInManager<ApplicationUser> signInManager,
                    IServiceScopeFactory scopeFactory,
                    ILoggerFactory loggerFactory) =>
                {
                    try
                    {
                        var result = await RevokeSessionAsync(
                            principal,
                            signInManager.UserManager,
                            scopeFactory);
                        if (!result.Succeeded)
                        {
                            var correlation = LogicLabProblemDetails
                                .CurrentCorrelationToken();
                            LogRevocationFailure(
                                loggerFactory.CreateLogger(LogCategory),
                                correlation,
                                string.Join(",", result.Errors.Select(error => error.Code)),
                                LogicLabProblemDetails.AuthenticationRevocationFailedCode,
                                exception: null);
                            return LogicLabProblemDetails.Create(
                                httpContext,
                                LogicLabProblemDetails.AuthenticationRevocationFailedCode,
                                correlation);
                        }

                        return Results.LocalRedirect("~/");
                    }
                    catch (Exception exception) when (IsRecoverable(exception))
                    {
                        var correlation = LogicLabProblemDetails
                            .CurrentCorrelationToken();
                        LogRevocationFailure(
                            loggerFactory.CreateLogger(LogCategory),
                            correlation,
                            identityErrorCodes: string.Empty,
                            LogicLabProblemDetails.AuthenticationRevocationFailedCode,
                            exception);
                        return LogicLabProblemDetails.Create(
                            httpContext,
                            LogicLabProblemDetails.AuthenticationRevocationFailedCode,
                            correlation);
                    }
                    finally
                    {
                        await signInManager.SignOutAsync();
                    }
                })
            .RequireAuthorization()
            .WithMetadata(new RequestSizeLimitAttribute(
                AccountIngressPolicy.MaximumRequestBodyBytes))
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true))
            .RequireRateLimiting(AccountIngressPolicy.LogoutRateLimitPolicyName)
            .WithMetadata(new RateLimitProblemDetailsMetadata(
                LogicLabProblemDetails.AuthenticationRateLimitExceededCode));
    }

    private static async Task<IdentityResult> RevokeSessionAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IServiceScopeFactory scopeFactory)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return IdentityResult.Success;
        }

        var result = await userManager.UpdateSecurityStampAsync(user);
        var concurrencyCode = userManager.ErrorDescriber.ConcurrencyFailure().Code;
        if (result.Succeeded
            || !result.Errors.Any(error => string.Equals(
                error.Code,
                concurrencyCode,
                StringComparison.Ordinal)))
        {
            return result;
        }

        await using var retryScope = scopeFactory.CreateAsyncScope();
        var retryManager = retryScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var currentUser = await retryManager.FindByIdAsync(user.Id);
        return currentUser is null
            ? IdentityResult.Success
            : await retryManager.UpdateSecurityStampAsync(currentUser);
    }

    private static bool IsRecoverable(Exception exception)
    {
        return exception is not (
            OutOfMemoryException
            or StackOverflowException
            or AccessViolationException);
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Authentication session revocation failed with correlation {Correlation}, Identity errors {IdentityErrorCodes}, and outcome {OutcomeCode}.")]
    private static partial void LogRevocationFailure(
        ILogger logger,
        string correlation,
        string identityErrorCodes,
        string outcomeCode,
        Exception? exception);
}
