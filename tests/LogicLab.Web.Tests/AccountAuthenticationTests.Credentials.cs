using System.Globalization;
using System.Net;
using LogicLab.Infrastructure.Identity;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LogicLab.Web.Tests;

internal sealed partial class AccountAuthenticationTests
{
    [Test]
    [RequiresPostgreSql]
    [Arguments("login")]
    [Arguments("register")]
    public async Task Post_IdentityEntryWhileAuthenticated_PreservesCurrentIdentity(string handler)
    {
        const string password = "Circuit-Passw0rd!";
        await using var database = await PostgreSqlIdentityTestDatabase.CreateAsync();
        var principalSource = new PrincipalSource();
        using var host = CreateIdentityHost(database.DataSource, principalSource);
        ApplicationUser currentUser;
        string currentStamp;
        IOptions<IdentityOptions> identityOptions;
        var replacementEmail = $"replacement-{Guid.CreateVersion7():N}@example.test";
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (currentUser, currentStamp, identityOptions) = await CreateUserAsync(
                scope.ServiceProvider,
                password: password);
            if (handler == "login")
            {
                _ = await CreateUserAsync(scope.ServiceProvider, replacementEmail, password);
            }
        }

        var currentAuthenticationState = AuthenticationStateFor(
            currentUser, currentStamp, identityOptions, Now.AddMinutes(5).ToString("O"));
        principalSource.Principal = currentAuthenticationState.User;
        using var client = host.CreateClient();
        var preparedForm = await WebTestHttp.GetAntiforgeryFormAsync(client, "/projects");
        List<KeyValuePair<string, string>> values =
        [
            new("Input.Email", replacementEmail),
            new("Input.Password", password),
            handler == "login"
                ? new("Input.RememberMe", "false")
                : new("Input.ConfirmPassword", password),
        ];
        using var response = await PostIdentityFormAsync(
            client, $"/account/{handler}?returnUrl=%2Fprojects", handler, preparedForm, values);

        await AssertCurrentIdentityPreservedAsync(
            host.Services, response, currentUser, currentStamp, currentAuthenticationState);
        if (handler == "register")
        {
            await using var verificationScope = host.Services.CreateAsyncScope();
            var userManager = verificationScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            await Assert.That(await userManager.FindByNameAsync(replacementEmail)).IsNull();
        }
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

        using var client = host.CreateClient();
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

        using var client = host.CreateClient();
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
        using var client = host.CreateClient();
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

        using var client = host.CreateClient();
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
        using var client = host.CreateClient();
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
        using var client = host.CreateClient();

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

        using var client = host.CreateClient();
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
        using var client = host.CreateClient();
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

        using var client = host.CreateClient();
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
        using var client = host.CreateClient();
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
        using var client = host.CreateClient();
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
}
