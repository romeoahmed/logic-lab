using System.Net;
using LogicLab.Infrastructure.Identity;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

internal sealed partial class AccountAuthenticationTests
{
    [Test]
    [RequiresPostgreSql]
    public async Task ValidateSessionAsync_ExpiryClaim_FailsClosedAtSessionBoundary()
    {
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        await using var scope = host.Services.CreateAsyncScope();
        var (user, stamp, identityOptions) = await CreateUserAsync(scope.ServiceProvider);
        var provider = scope.ServiceProvider
            .GetRequiredService<AuthenticationStateProvider>();

        var missing = await ValidateAsync(
            provider,
            AuthenticationStateFor(user, stamp, identityOptions, expiresUtc: null));
        var malformed = await ValidateAsync(
            provider,
            AuthenticationStateFor(user, stamp, identityOptions, "not-a-date"));
        var exactlyExpired = await ValidateAsync(
            provider,
            AuthenticationStateFor(user, stamp, identityOptions, Now.ToString("O")));
        var expired = await ValidateAsync(
            provider,
            AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                Now.AddSeconds(-1).ToString("O")));
        var future = await ValidateAsync(
            provider,
            AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                Now.AddMinutes(5).ToString("O")));

        using (Assert.Multiple())
        {
            await Assert.That(missing).IsFalse();
            await Assert.That(malformed).IsFalse();
            await Assert.That(exactlyExpired).IsFalse();
            await Assert.That(expired).IsFalse();
            await Assert.That(future).IsTrue();
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_Logout_EstablishedCircuitPrincipal_IsRevoked()
    {
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource);
        ApplicationUser user;
        string oldStamp;
        IOptions<IdentityOptions> identityOptions;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (user, oldStamp, identityOptions) = await CreateUserAsync(
                scope.ServiceProvider);
        }

        var oldAuthenticationState = AuthenticationStateFor(
            user,
            oldStamp,
            identityOptions,
            Now.AddMinutes(5).ToString("O"));
        principalSource.Principal = oldAuthenticationState.User;
        using var client = host.CreateClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/account/logout", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", form.RequestToken),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);

        using var response = await client.SendAsync(request);

        await using var verificationScope = host.Services.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var storedUser = await userManager.FindByIdAsync(user.Id)
            ?? throw new InvalidOperationException("The test user disappeared.");
        var currentStamp = await userManager.GetSecurityStampAsync(storedUser);
        var provider = verificationScope.ServiceProvider
            .GetRequiredService<AuthenticationStateProvider>();
        var oldCircuitIsValid = await ValidateAsync(provider, oldAuthenticationState);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(currentStamp).IsNotEqualTo(oldStamp);
            await Assert.That(oldCircuitIsValid).IsFalse();
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_Logout_RevocationInfrastructureFails_ClearsCookieAndReturnsProblemDetails()
    {
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var logs = new FakeLoggerProvider();
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource,
            logs);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(scope.ServiceProvider);
            principalSource.Principal = AuthenticationStateFor(
                created.User,
                created.SecurityStamp,
                created.Options,
                Now.AddMinutes(5).ToString("O")).User;
        }

        using var client = host.CreateClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        await database.StopAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/account/logout", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", form.RequestToken),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);

        using var response = await client.SendAsync(request);
        var applicationCookieDeleted = response.Headers.TryGetValues(
                "Set-Cookie",
                out var cookieHeaders)
            && cookieHeaders.Any(value => value.StartsWith(
                    ".AspNetCore.Identity.Application=;",
                    StringComparison.Ordinal)
                && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "authentication_revocation_failed");
        await Assert.That(applicationCookieDeleted).IsTrue();
        var log = logs.Collector.GetSnapshot().Single(record => record.Id.Id == 2001);
        await Assert.That(log.Exception).IsNull();
        await Assert.That(log.GetStructuredStateValue("OutcomeCode"))
            .IsEqualTo("authentication_revocation_failed");
        await Assert.That(log.Message).DoesNotContain("Npgsql");
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_Logout_RequestBodyLimitIsInclusiveAndPreventsAdditionalRevocation()
    {
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource);
        ApplicationUser user;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(scope.ServiceProvider);
            user = created.User;
            principalSource.Principal = AuthenticationStateFor(
                created.User,
                created.SecurityStamp,
                created.Options,
                Now.AddMinutes(5).ToString("O")).User;
        }

        using var client = host.CreateClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        using var accepted = await PostSizedLogoutFormAsync(
            client,
            form,
            AccountIngressPolicy.MaximumRequestBodyBytes);
        var stampAfterAccepted = await ReadSecurityStampAsync(host.Services, user.Id);
        using var rejected = await PostSizedLogoutFormAsync(
            client,
            form,
            AccountIngressPolicy.MaximumRequestBodyBytes + 1);
        var stampAfterRejected = await ReadSecurityStampAsync(host.Services, user.Id);

        using (Assert.Multiple())
        {
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await WebTestHttp.AssertProblemDetailsAsync(
                rejected,
                HttpStatusCode.RequestEntityTooLarge,
                "request_body_too_large");
            await Assert.That(stampAfterRejected).IsEqualTo(stampAfterAccepted);
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_Logout_RateLimitRejectsBeforeAdditionalRevocation()
    {
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource);
        ApplicationUser user;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(scope.ServiceProvider);
            user = created.User;
            principalSource.Principal = AuthenticationStateFor(
                created.User,
                created.SecurityStamp,
                created.Options,
                Now.AddMinutes(5).ToString("O")).User;
        }

        using var client = host.CreateClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        for (var attempt = 0;
             attempt < AccountIngressPolicy.Default.LogoutPermitLimit;
             attempt++)
        {
            using var accepted = await PostSizedLogoutFormAsync(
                client,
                form,
                AccountIngressPolicy.MaximumRequestBodyBytes);
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        }

        var stampAfterAccepted = await ReadSecurityStampAsync(host.Services, user.Id);
        using var rejected = await PostSizedLogoutFormAsync(
            client,
            form,
            AccountIngressPolicy.MaximumRequestBodyBytes);
        var stampAfterRejected = await ReadSecurityStampAsync(host.Services, user.Id);
        var retryAfter = rejected.Headers.RetryAfter?.Delta;

        using (Assert.Multiple())
        {
            await WebTestHttp.AssertProblemDetailsAsync(
                rejected,
                HttpStatusCode.TooManyRequests,
                "authentication_rate_limit_exceeded");
            await Assert.That(retryAfter).IsNotNull();
            await Assert.That(retryAfter.GetValueOrDefault())
                .IsGreaterThan(TimeSpan.Zero);
            await Assert.That(stampAfterRejected).IsEqualTo(stampAfterAccepted);
        }
    }
}
