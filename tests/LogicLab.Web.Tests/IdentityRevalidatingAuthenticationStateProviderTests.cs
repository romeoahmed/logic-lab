using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Data;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>(Shared = SharedType.PerTestSession)]
internal sealed class IdentityRevalidatingAuthenticationStateProviderTests(
    LogicLabWebFactory factory)
{
    private const string AuthenticationExpiryClaimType =
        "logiclab:authentication_expires_utc";

    [Test]
    public async Task ValidateAuthenticationStateAsync_ExpiryClaim_FailsClosedAtSessionBoundary()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        using var host = CreateIdentityHost(connection, new FixedTimeProvider(now));
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
            AuthenticationStateFor(user, stamp, identityOptions, now.ToString("O")));
        var expired = await ValidateAsync(
            provider,
            AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                now.AddSeconds(-1).ToString("O")));
        var future = await ValidateAsync(
            provider,
            AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                now.AddMinutes(5).ToString("O")));

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
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var expiresUtc = now.AddMinutes(5);
        await using var connection = await OpenIdentityDatabaseAsync();
        using var host = CreateIdentityHost(connection, new FixedTimeProvider(now));
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
            await Assert.That(options.SlidingExpiration).IsFalse();
            await Assert.That(expiryClaims).Count().IsEqualTo(1);
            await Assert.That(expiryClaims.Single().Value)
                .IsEqualTo(expiresUtc.ToString("O"));
        }
    }

    [Test]
    public async Task ApplicationCookie_SecurityStampRefresh_PreservesAbsoluteExpiryClaim()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var expiresUtc = now.AddMinutes(5);
        await using var connection = await OpenIdentityDatabaseAsync();
        using var host = CreateIdentityHost(connection, new FixedTimeProvider(now));
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
            IssuedUtc = now.AddMinutes(-5),
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
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        using var host = CreateIdentityHost(connection, new FixedTimeProvider(now));
        await using var scope = host.Services.CreateAsyncScope();
        var (user, stamp, identityOptions) = await CreateUserAsync(scope.ServiceProvider);
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
            now.ToString("O"),
            now.AddSeconds(-1).ToString("O"),
        ];

        foreach (var invalidExpiry in invalidExpiries)
        {
            var principal = AuthenticationStateFor(
                user,
                stamp,
                identityOptions,
                invalidExpiry).User;
            var context = new CookieValidatePrincipalContext(
                new DefaultHttpContext { RequestServices = scope.ServiceProvider },
                scheme,
                options,
                new AuthenticationTicket(
                    principal,
                    new AuthenticationProperties
                    {
                        IssuedUtc = now.AddMinutes(-5),
                        ExpiresUtc = now.AddMinutes(5),
                    },
                    IdentityConstants.ApplicationScheme));

            await options.Events.ValidatePrincipal(context);

            await Assert.That(context.Principal).IsNull();
        }
    }

    [Test]
    public async Task AuthenticationRevalidation_IntervalsBoundEstablishedCircuitRevocationDelay()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        using var host = CreateIdentityHost(connection, new FixedTimeProvider(now));
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider
            .GetRequiredService<AuthenticationStateProvider>();
        var intervalProperty = provider.GetType().GetProperty(
            "RevalidationInterval",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "The revalidating provider does not expose its interval seam.");
        var circuitInterval = (TimeSpan)(intervalProperty.GetValue(provider)
            ?? throw new InvalidOperationException("The circuit interval was null."));
        var stampInterval = scope.ServiceProvider
            .GetRequiredService<IOptions<SecurityStampValidatorOptions>>()
            .Value
            .ValidationInterval;

        using (Assert.Multiple())
        {
            await Assert.That(circuitInterval).IsEqualTo(TimeSpan.FromMinutes(5));
            await Assert.That(stampInterval).IsEqualTo(TimeSpan.FromMinutes(4));
        }
    }

    [Test]
    public async Task Post_Logout_EstablishedCircuitPrincipal_IsRevoked()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            connection,
            new FixedTimeProvider(now),
            principalSource,
            configureAuthenticatedHttp: true);
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
            now.AddMinutes(5).ToString("O"));
        principalSource.Principal = oldAuthenticationState.User;
        using var client = host.CreateHttpsClient();
        using var pageResponse = await client.GetAsync(
            new Uri("/projects", UriKind.Relative));
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();
        var requestToken = ExtractAttributeAfter(
            html,
            "name=\"__RequestVerificationToken\"",
            "value");
        var antiforgeryCookie = pageResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal))
            .Split(';', 2)[0];
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/account/logout", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", requestToken),
            ]),
        };
        request.Headers.Add("Cookie", antiforgeryCookie);

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
    public async Task Post_LoginWhileAuthenticated_FailsClosedWithoutIdentitySwitch()
    {
        const string password = "Circuit-Passw0rd!";
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            connection,
            new FixedTimeProvider(now),
            principalSource,
            configureAuthenticatedHttp: true);
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
            now.AddMinutes(5).ToString("O"));
        principalSource.Principal = currentAuthenticationState.User;
        using var client = host.CreateHttpsClient();
        var preparedForm = await GetIdentityFormAsync(client, "/projects");

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
    public async Task Post_RegisterWhileAuthenticated_FailsClosedWithoutIdentitySwitch()
    {
        const string password = "Circuit-Passw0rd!";
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            connection,
            new FixedTimeProvider(now),
            principalSource,
            configureAuthenticatedHttp: true);
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
            now.AddMinutes(5).ToString("O"));
        principalSource.Principal = currentAuthenticationState.User;
        var replacementEmail = $"registered-{Guid.CreateVersion7():N}@example.test";
        using var client = host.CreateHttpsClient();
        var preparedForm = await GetIdentityFormAsync(client, "/projects");

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
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        await using var connection = await OpenIdentityDatabaseAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(
            connection,
            new FixedTimeProvider(now),
            principalSource,
            configureAuthenticatedHttp: true);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var (user, stamp, options) = await CreateUserAsync(scope.ServiceProvider);
            principalSource.Principal = AuthenticationStateFor(
                user,
                stamp,
                options,
                now.AddMinutes(5).ToString("O")).User;
        }

        using var client = host.CreateHttpsClient();
        using var response = await client.GetAsync(
            new Uri($"{path}?returnUrl=%2Fprojects", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(RedirectPath(response))
                .IsEqualTo("/projects");
            await Assert.That(html).DoesNotContain("<form");
        }
    }

    private WebApplicationFactory<Program> CreateIdentityHost(
        SqliteConnection connection,
        TimeProvider timeProvider,
        PrincipalSource? principalSource = null,
        bool configureAuthenticatedHttp = false)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationIdentityDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationIdentityDbContext>>();
                services.AddDbContext<ApplicationIdentityDbContext>(options =>
                    options.UseSqlite(connection));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
                if (!configureAuthenticatedHttp)
                {
                    return;
                }

                services.AddSingleton(principalSource!);
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

    private static async Task<SqliteConnection> OpenIdentityDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationIdentityDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return connection;
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

    private static async Task<bool> ValidateAsync(
        AuthenticationStateProvider provider,
        AuthenticationState authenticationState)
    {
        var method = provider.GetType().GetMethod(
            "ValidateAuthenticationStateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "The revalidating provider does not expose its validation seam.");
        var task = method.Invoke(
            provider,
            [authenticationState, CancellationToken.None]) as Task<bool>
            ?? throw new InvalidOperationException(
                "The revalidating provider returned an unexpected validation task.");
        return await task;
    }

    private static async Task<PreparedIdentityForm> GetIdentityFormAsync(
        HttpClient client,
        string path)
    {
        using var pageResponse = await client.GetAsync(
            new Uri(path, UriKind.Relative));
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();
        var requestToken = ExtractAttributeAfter(
            html,
            "name=\"__RequestVerificationToken\"",
            "value");
        var antiforgeryCookie = pageResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return new PreparedIdentityForm(requestToken, antiforgeryCookie);
    }

    private static async Task<HttpResponseMessage> PostIdentityFormAsync(
        HttpClient client,
        string path,
        string formName,
        PreparedIdentityForm preparedForm,
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
        request.Headers.Add("Cookie", preparedForm.AntiforgeryCookie);
        return await client.SendAsync(request);
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

    private sealed record PreparedIdentityForm(
        string RequestToken,
        string AntiforgeryCookie);

    private static string ExtractAttributeAfter(
        string html,
        string marker,
        string attributeName)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException($"Markup did not contain {marker}.");
        }

        var prefix = $"{attributeName}=\"";
        var valueStart = html.IndexOf(prefix, markerIndex, StringComparison.Ordinal);
        if (valueStart < 0)
        {
            throw new InvalidOperationException(
                $"Markup did not contain {attributeName} after {marker}.");
        }

        valueStart += prefix.Length;
        var valueEnd = html.IndexOf('"', valueStart);
        return html[valueStart..valueEnd];
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
            DurableProjectCatalogCallContext context,
            DurableProjectPageRequest request,
            CancellationToken cancellationToken)
        {
            DurableProjectListOutcome outcome = new DurableProjectPage([], next: null);
            return Task.FromResult(outcome);
        }
    }
}
