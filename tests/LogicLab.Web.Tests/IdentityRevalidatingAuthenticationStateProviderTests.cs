using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Identity;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>]
internal sealed class IdentityRevalidatingAuthenticationStateProviderTests(
    LogicLabWebFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private const string AuthenticationExpiryClaimType =
        "logiclab:authentication_expires_utc";

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
    public async Task ApplicationCookie_SigningIn_EmbedsAbsoluteExpiryClaim()
    {
        var expiresUtc = Now.AddMinutes(5);
        using var host = CreateIdentityHost();
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "cookie-user")],
            IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = expiresUtc,
        };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        var context = new CookieSigningInContext(
            new DefaultHttpContext { RequestServices = host.Services },
            scheme,
            options,
            principal,
            properties,
            new CookieOptions());

        await options.Events.SigningIn(context);
        var expiryClaims = context.Principal!
            .FindAll(AuthenticationExpiryClaimType)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(expiryClaims).Count().IsEqualTo(1);
            await Assert.That(expiryClaims.Single().Value)
                .IsEqualTo(expiresUtc.ToString("O"));
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task ApplicationCookie_SecurityStampRefresh_PreservesAbsoluteExpiryClaim()
    {
        var expiresUtc = Now.AddMinutes(5);
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        await using var scope = host.Services.CreateAsyncScope();
        var (user, stamp, identityOptions) = await CreateUserAsync(scope.ServiceProvider);
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var principal = AuthenticationStateFor(
            user,
            stamp,
            identityOptions,
            expiresUtc.ToString("O")).User;
        var properties = new AuthenticationProperties
        {
            IssuedUtc = Now.AddMinutes(-5),
            ExpiresUtc = expiresUtc,
        };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            scheme,
            options,
            new AuthenticationTicket(
                principal,
                properties,
                IdentityConstants.ApplicationScheme));

        await options.Events.ValidatePrincipal(context);
        var expiryClaims = context.Principal!
            .FindAll(AuthenticationExpiryClaimType)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(context.ShouldRenew).IsTrue();
            await Assert.That(expiryClaims).Count().IsEqualTo(1);
            await Assert.That(expiryClaims.Single().Value)
                .IsEqualTo(expiresUtc.ToString("O"));
        }
    }

    [Test]
    public async Task ApplicationCookie_InvalidAbsoluteExpiry_RejectsHttpPrincipal()
    {
        using var host = CreateIdentityHost();
        var identityOptions = host.Services
            .GetRequiredService<IOptions<IdentityOptions>>();
        var user = new ApplicationUser { Id = "expiry-user" };
        const string stamp = "expiry-stamp";
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        string?[] invalidExpiries =
        [
            null,
            "not-a-date",
            Now.ToString("O"),
            Now.AddSeconds(-1).ToString("O"),
        ];

        foreach (var invalidExpiry in invalidExpiries)
        {
            var principal = AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                invalidExpiry).User;
            var context = new CookieValidatePrincipalContext(
                new DefaultHttpContext { RequestServices = host.Services },
                scheme,
                options,
                new AuthenticationTicket(
                    principal,
                    new AuthenticationProperties
                    {
                        IssuedUtc = Now.AddMinutes(-5),
                        ExpiresUtc = Now.AddMinutes(5),
                    },
                    IdentityConstants.ApplicationScheme));

            await options.Events.ValidatePrincipal(context);

            await Assert.That(context.Principal).IsNull();
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
        using var client = host.CreateHttpsClient();
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
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(scope.ServiceProvider);
            principalSource.Principal = AuthenticationStateFor(
                created.User,
                created.SecurityStamp,
                created.Options,
                Now.AddMinutes(5).ToString("O")).User;
        }

        using var client = host.CreateHttpsClient();
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

        using var client = host.CreateHttpsClient();
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

        using var client = host.CreateHttpsClient();
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

    [Test]
    [RequiresPostgreSql]
    public async Task Post_LoginWhileAuthenticated_FailsClosedWithoutIdentitySwitch()
    {
        const string password = "Circuit-Passw0rd!";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource);
        ApplicationUser currentUser;
        string currentStamp;
        IOptions<IdentityOptions> identityOptions;
        var replacementEmail = $"replacement-{Guid.CreateVersion7():N}@example.test";
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (currentUser, currentStamp, identityOptions) = await CreateUserAsync(
                scope.ServiceProvider,
                password: password);
            _ = await CreateUserAsync(
                scope.ServiceProvider,
                email: replacementEmail,
                password: password);
        }

        var currentAuthenticationState = AuthenticationStateFor(
            currentUser,
            currentStamp,
            identityOptions,
            Now.AddMinutes(5).ToString("O"));
        principalSource.Principal = currentAuthenticationState.User;
        using var client = host.CreateHttpsClient();
        var preparedForm = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/projects");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/login?returnUrl=%2Fprojects",
            "login",
            preparedForm,
            [
                new("Input.Email", replacementEmail),
                new("Input.Password", password),
                new("Input.RememberMe", "false"),
            ]);

        await AssertCurrentIdentityPreservedAsync(
            host.Services,
            response,
            currentUser,
            currentStamp,
            currentAuthenticationState);
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_RegisterWhileAuthenticated_FailsClosedWithoutIdentitySwitch()
    {
        const string password = "Circuit-Passw0rd!";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            database.DataSource,
            principalSource);
        ApplicationUser currentUser;
        string currentStamp;
        IOptions<IdentityOptions> identityOptions;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (currentUser, currentStamp, identityOptions) = await CreateUserAsync(
                scope.ServiceProvider,
                password: password);
        }

        var currentAuthenticationState = AuthenticationStateFor(
            currentUser,
            currentStamp,
            identityOptions,
            Now.AddMinutes(5).ToString("O"));
        principalSource.Principal = currentAuthenticationState.User;
        var replacementEmail = $"registered-{Guid.CreateVersion7():N}@example.test";
        using var client = host.CreateHttpsClient();
        var preparedForm = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/projects");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/register?returnUrl=%2Fprojects",
            "register",
            preparedForm,
            [
                new("Input.Email", replacementEmail),
                new("Input.Password", password),
                new("Input.ConfirmPassword", password),
            ]);

        await AssertCurrentIdentityPreservedAsync(
            host.Services,
            response,
            currentUser,
            currentStamp,
            currentAuthenticationState);
        await using var verificationScope = host.Services.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        await Assert.That(await userManager.FindByNameAsync(replacementEmail))
            .IsNull();
    }

    [Test]
    [Arguments("/account/login")]
    [Arguments("/account/register")]
    public async Task Get_IdentityEntryWhileAuthenticated_RedirectsWithoutRenderingSwitchForm(
        string path)
    {
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            principalSource: principalSource);
        var identityOptions = host.Services
            .GetRequiredService<IOptions<IdentityOptions>>();
        principalSource.Principal = AuthenticationStateFor(
            new ApplicationUser { Id = "authenticated-user" },
            "authenticated-stamp",
            identityOptions,
            Now.AddMinutes(5).ToString("O")).User;

        using var client = host.CreateHttpsClient();
        using var response = await client.GetAsync(
            new Uri($"{path}?returnUrl=%2Fprojects", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();
        var document = WebTestMarkup.Parse(html);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(RedirectPath(response))
                .IsEqualTo("/projects");
            await Assert.That(document.QuerySelectorAll("form")).IsEmpty();
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_LoginWithInvalidCredentials_DoesNotEchoSubmittedPassword()
    {
        const string password = "Correct-Passw0rd!";
        const string submittedPassword = "Wrong-Passw0rd!";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        string email;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(
                scope.ServiceProvider,
                password: password);
            email = created.User.Email!;
        }

        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");
        using var response = await PostIdentityFormAsync(
            client,
            "/account/login",
            "login",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", submittedPassword),
                new("Input.RememberMe", "false"),
            ]);
        var html = await response.Content.ReadAsStringAsync();
        var document = WebTestMarkup.Parse(html);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(document.QuerySelectorAll("[role='alert']"))
                .IsNotEmpty();
            await Assert.That(html).DoesNotContain(submittedPassword);
        }
    }

    [Test]
    public async Task Post_LoginWithInvalidModel_DoesNotEchoSubmittedPassword()
    {
        const string submittedPassword = "Invalid-Model-Passw0rd!";
        using var host = CreateIdentityHost();
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/login",
            "login",
            form,
            [
                new("Input.Email", "not-an-email"),
                new("Input.Password", submittedPassword),
                new("Input.RememberMe", "false"),
            ]);
        var html = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(html).DoesNotContain(submittedPassword);
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_RegisterWithDuplicateEmail_DoesNotEchoSubmittedPasswords()
    {
        const string existingPassword = "Existing-Passw0rd!";
        const string submittedPassword = "Replacement-Passw0rd!";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        string email;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(
                scope.ServiceProvider,
                password: existingPassword);
            email = created.User.Email!;
        }

        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/register");
        using var response = await PostIdentityFormAsync(
            client,
            "/account/register",
            "register",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", submittedPassword),
                new("Input.ConfirmPassword", submittedPassword),
            ]);
        var html = await response.Content.ReadAsStringAsync();
        var document = WebTestMarkup.Parse(html);
        await using var verificationScope = host.Services.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var storedUser = await userManager.FindByNameAsync(email);
        var originalPasswordRemainsValid = storedUser is not null
            && await userManager.CheckPasswordAsync(storedUser, existingPassword);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(html).DoesNotContain(submittedPassword);
            await Assert.That(document.QuerySelectorAll("[role='alert']"))
                .IsNotEmpty();
            await Assert.That(storedUser).IsNotNull();
            await Assert.That(originalPasswordRemainsValid).IsTrue();
        }
    }

    [Test]
    public async Task Post_RegisterWithInvalidModel_DoesNotEchoSubmittedPasswords()
    {
        const string password = "Valid-Passw0rd!";
        const string confirmation = "Different-Passw0rd!";
        using var host = CreateIdentityHost();
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/register");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/register",
            "register",
            form,
            [
                new("Input.Email", "new-user@example.test"),
                new("Input.Password", password),
                new("Input.ConfirmPassword", confirmation),
            ]);
        var html = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(html).DoesNotContain(password);
            await Assert.That(html).DoesNotContain(confirmation);
        }
    }

    [Test]
    [Arguments(
        "/account/login",
        "Input.Email",
        AccountInputLimits.MaximumEmailLength)]
    [Arguments(
        "/account/login",
        "Input.Password",
        AccountInputLimits.MaximumPasswordLength)]
    [Arguments(
        "/account/register",
        "Input.Email",
        AccountInputLimits.MaximumEmailLength)]
    [Arguments(
        "/account/register",
        "Input.Password",
        AccountInputLimits.MaximumPasswordLength)]
    [Arguments(
        "/account/register",
        "Input.ConfirmPassword",
        AccountInputLimits.MaximumPasswordLength)]
    public async Task Get_IdentityEntry_RendersApplicationMaximumLength(
        string path,
        string fieldName,
        int expectedMaximum)
    {
        using var host = CreateIdentityHost();
        using var client = host.CreateHttpsClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var field = WebTestMarkup.RequireElement(
            WebTestMarkup.Parse(html),
            $"[name='{fieldName}']");

        await Assert.That(field.GetAttribute("maxlength"))
            .IsEqualTo(expectedMaximum.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_LoginAtApplicationLimits_AuthenticatesIdentity()
    {
        const string emailSuffix = "@example.test";
        const string passwordPrefix = "A1!";
        var email = $"{new string(
            'a',
            AccountInputLimits.MaximumEmailLength - emailSuffix.Length)}{emailSuffix}";
        var password = $"{passwordPrefix}{new string(
            'x',
            AccountInputLimits.MaximumPasswordLength - passwordPrefix.Length)}";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        string userId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(
                scope.ServiceProvider,
                email,
                password);
            userId = created.User.Id;
        }

        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");
        using var response = await PostIdentityFormAsync(
            client,
            "/account/login?returnUrl=%2Fprojects",
            "login",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", password),
                new("Input.RememberMe", "false"),
            ]);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(RedirectPath(response)).IsEqualTo("/projects");
            await AssertAuthenticationCookieAsync(host.Services, response, userId);
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_RegisterAtApplicationLimits_CreatesIdentity()
    {
        const string emailSuffix = "@example.test";
        const string passwordPrefix = "A1!";
        var email = $"{new string(
            'b',
            AccountInputLimits.MaximumEmailLength - emailSuffix.Length)}{emailSuffix}";
        var password = $"{passwordPrefix}{new string(
            'y',
            AccountInputLimits.MaximumPasswordLength - passwordPrefix.Length)}";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/register");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/register?returnUrl=%2Fprojects",
            "register",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", password),
                new("Input.ConfirmPassword", password),
            ]);
        await using var verificationScope = host.Services.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var storedUser = await userManager.FindByNameAsync(email);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(RedirectPath(response)).IsEqualTo("/projects");
            await Assert.That(storedUser).IsNotNull();
            await AssertAuthenticationCookieAsync(host.Services, response, storedUser!.Id);
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_LoginWithOversizedPassword_DoesNotReachIdentityPasswordCheck()
    {
        const string password = "Correct-Passw0rd!";
        var submittedPassword = new string(
            'x',
            AccountInputLimits.MaximumPasswordLength + 1);
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        string userId;
        string email;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var created = await CreateUserAsync(
                scope.ServiceProvider,
                password: password);
            created.User.LockoutEnabled = true;
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var updated = await userManager.UpdateAsync(created.User);
            if (!updated.Succeeded)
            {
                throw new InvalidOperationException(string.Join(
                    ", ",
                    updated.Errors.Select(error => error.Code)));
            }

            userId = created.User.Id;
            email = created.User.Email!;
        }

        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");
        using var response = await PostIdentityFormAsync(
            client,
            "/account/login",
            "login",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", submittedPassword),
                new("Input.RememberMe", "false"),
            ]);
        var html = await response.Content.ReadAsStringAsync();
        await using var verificationScope = host.Services.CreateAsyncScope();
        var verificationManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var storedUser = await verificationManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("The test user disappeared.");

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(storedUser.AccessFailedCount).IsEqualTo(0);
            await Assert.That(html).DoesNotContain(submittedPassword);
        }
    }

    [Test]
    public async Task Post_LoginWithOversizedEmail_UsesApplicationValidationBoundary()
    {
        const string password = "Bounded-Passw0rd!";
        const string emailSuffix = "@example.test";
        var email = $"{new string(
            'a',
            AccountInputLimits.MaximumEmailLength + 1 - emailSuffix.Length)}{emailSuffix}";
        using var host = CreateIdentityHost();
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/login");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/login",
            "login",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", password),
                new("Input.RememberMe", "false"),
            ]);
        var html = await response.Content.ReadAsStringAsync();
        var emailValidation = WebTestMarkup.RequireElement(
            WebTestMarkup.Parse(html),
            "#login-email-validation");

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(string.IsNullOrWhiteSpace(emailValidation.TextContent))
                .IsFalse();
            await Assert.That(html).DoesNotContain(password);
        }
    }

    [Test]
    [RequiresPostgreSql]
    public async Task Post_RegisterWithOversizedEmail_DoesNotCreateIdentity()
    {
        const string password = "Bounded-Passw0rd!";
        const string emailSuffix = "@example.test";
        var email = $"{new string(
            'a',
            AccountInputLimits.MaximumEmailLength + 1 - emailSuffix.Length)}{emailSuffix}";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        using var host = CreateIdentityHost(database.DataSource);
        using var client = host.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(
            client,
            "/account/register");

        using var response = await PostIdentityFormAsync(
            client,
            "/account/register",
            "register",
            form,
            [
                new("Input.Email", email),
                new("Input.Password", password),
                new("Input.ConfirmPassword", password),
            ]);
        var html = await response.Content.ReadAsStringAsync();
        await using var verificationScope = host.Services.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await userManager.FindByNameAsync(email)).IsNull();
            await Assert.That(html).DoesNotContain(password);
        }
    }

    private WebApplicationFactory<Program> CreateIdentityHost(
        NpgsqlDataSource? identityDataSource = null,
        PrincipalSource? principalSource = null)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                if (identityDataSource is not null)
                {
                    services.RemoveAll<ApplicationIdentityDbContext>();
                    services.RemoveAll<
                        DbContextOptions<ApplicationIdentityDbContext>>();
                    services.RemoveAll<
                        IDbContextOptionsConfiguration<
                            ApplicationIdentityDbContext>>();
                    services.AddDbContext<ApplicationIdentityDbContext>(options =>
                        options.UseNpgsql(identityDataSource));
                }

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
                if (principalSource is null)
                {
                    return;
                }

                services.AddSingleton(principalSource);
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme =
                            PrincipalAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme =
                            PrincipalAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions,
                        PrincipalAuthenticationHandler>(
                        PrincipalAuthenticationHandler.SchemeName,
                        configureOptions: null);
                services.RemoveAll<IDurableProjectCatalog>();
                services.AddSingleton<IDurableProjectCatalog>(new EmptyCatalog());
            }));
    }

    private static async Task<(
        ApplicationUser User,
        string SecurityStamp,
        IOptions<IdentityOptions> Options)> CreateUserAsync(
        IServiceProvider services,
        string? email = null,
        string? password = null)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        email ??= $"circuit-{Guid.CreateVersion7():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
        };
        var created = password is null
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join(
                ", ",
                created.Errors.Select(error => error.Code)));
        }

        return (
            user,
            await userManager.GetSecurityStampAsync(user),
            services.GetRequiredService<IOptions<IdentityOptions>>());
    }

    private static AuthenticationState AuthenticationStateFor(
        ApplicationUser user,
        string securityStamp,
        IOptions<IdentityOptions> options,
        string? expiresUtc)
    {
        var claims = new List<Claim>
        {
            new(options.Value.ClaimsIdentity.UserIdClaimType, user.Id),
            new(options.Value.ClaimsIdentity.SecurityStampClaimType, securityStamp),
        };
        if (expiresUtc is not null)
        {
            claims.Add(new Claim(AuthenticationExpiryClaimType, expiresUtc));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            IdentityConstants.ApplicationScheme)));
    }

    private static Task<bool> ValidateAsync(
        AuthenticationStateProvider provider,
        AuthenticationState authenticationState)
    {
        return ((IdentityRevalidatingAuthenticationStateProvider)provider)
            .ValidateSessionAsync(authenticationState, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PostIdentityFormAsync(
        HttpClient client,
        string path,
        string formName,
        AntiforgeryForm preparedForm,
        IReadOnlyList<KeyValuePair<string, string>> formValues)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("_handler", formName),
            new("__RequestVerificationToken", preparedForm.RequestToken),
        };
        values.AddRange(formValues);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(path, UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(values),
        };
        request.Headers.Add("Cookie", preparedForm.Cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostSizedLogoutFormAsync(
        HttpClient client,
        AntiforgeryForm preparedForm,
        int bodyLength)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/account/logout", UriKind.Relative))
        {
            Content = WebTestHttp.CreateSizedFormContent(
                bodyLength,
                new KeyValuePair<string, string>("__RequestVerificationToken", preparedForm.RequestToken)),
        };
        request.Headers.Add("Cookie", preparedForm.Cookie);
        return await client.SendAsync(request);
    }

    private static async Task AssertAuthenticationCookieAsync(
        IServiceProvider services,
        HttpResponseMessage response,
        string expectedUserId)
    {
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(header => header.Split(';', 2)[0]));
        var cookie = options.CookieManager.GetRequestCookie(context, options.Cookie.Name!);
        await Assert.That(cookie).IsNotNull();
        var ticket = options.TicketDataFormat.Unprotect(cookie!);
        await Assert.That(ticket).IsNotNull();
        await Assert.That(ticket!.Principal.Identity!.IsAuthenticated).IsTrue();
        await Assert.That(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo(expectedUserId);
    }

    private static async Task<string> ReadSecurityStampAsync(
        IServiceProvider services,
        string userId)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("The test user disappeared.");
        return await userManager.GetSecurityStampAsync(user);
    }

    private static async Task AssertCurrentIdentityPreservedAsync(
        IServiceProvider services,
        HttpResponseMessage response,
        ApplicationUser currentUser,
        string currentStamp,
        AuthenticationState currentAuthenticationState)
    {
        await using var verificationScope = services.CreateAsyncScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var storedCurrentUser = await userManager.FindByIdAsync(currentUser.Id)
            ?? throw new InvalidOperationException("The current user disappeared.");
        var storedStamp = await userManager.GetSecurityStampAsync(storedCurrentUser);
        var provider = verificationScope.ServiceProvider
            .GetRequiredService<AuthenticationStateProvider>();
        var currentCircuitIsValid = await ValidateAsync(
            provider,
            currentAuthenticationState);
        var replacementCookieIssued = response.Headers.TryGetValues(
                "Set-Cookie",
                out var cookieHeaders)
            && cookieHeaders.Any(value => value.Contains(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(RedirectPath(response)).IsEqualTo("/projects");
            await Assert.That(storedStamp).IsEqualTo(currentStamp);
            await Assert.That(currentCircuitIsValid).IsTrue();
            await Assert.That(replacementCookieIssued).IsFalse();
        }
    }

    private static string? RedirectPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        return location?.IsAbsoluteUri is true
            ? location.PathAndQuery
            : location?.OriginalString;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class PrincipalSource
    {
        public ClaimsPrincipal? Principal { get; set; }
    }

    private sealed class PrincipalAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PrincipalSource source)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "CircuitTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (source.Principal is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var ticket = new AuthenticationTicket(source.Principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class EmptyCatalog : IDurableProjectCatalog
    {
        public Task<DurableProjectListOutcome> ListAsync(
            AuthenticatedSubjectId subjectId,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            DurableProjectListOutcome outcome = new DurableProjectPage([], next: null);
            return Task.FromResult(outcome);
        }
    }
}
