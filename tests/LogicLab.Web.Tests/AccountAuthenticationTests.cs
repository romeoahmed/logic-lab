using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Identity;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
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
internal sealed partial class AccountAuthenticationTests(
    LogicLabWebFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private const string AuthenticationExpiryClaimType =
        "logiclab:authentication_expires_utc";

    private WebApplicationFactory<Program> CreateIdentityHost(
        NpgsqlDataSource? identityDataSource = null,
        PrincipalSource? principalSource = null,
        ILoggerProvider? loggerProvider = null)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                if (loggerProvider is not null)
                {
                    services.AddLogging(builder => builder.AddProvider(loggerProvider));
                }

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
