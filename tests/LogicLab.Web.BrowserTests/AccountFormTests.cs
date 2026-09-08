using LogicLab.Web.Testing;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabKestrelApplication>(Shared = SharedType.PerClass)]
internal sealed class AccountFormTests(LogicLabKestrelApplication application) : PageTest
{
    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;
        options.JavaScriptEnabled = false;
        return options;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Login_InvalidFormWithoutJavaScript_PreservesRememberChoiceAndClearsPassword(
        bool remember)
    {
        await Page.GotoAsync(new Uri(application.EditorUri, "/account/login").AbsoluteUri);
        var checkbox = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Remember me" });
        var password = Page.GetByLabel("Password", new() { Exact = true });
        await Expect(checkbox).Not.ToBeCheckedAsync();
        await checkbox.SetCheckedAsync(remember);
        await password.FillAsync("Test-password-42!");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();

        await Expect(Page.GetByText("The Email field is required.", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(password).ToHaveValueAsync(string.Empty);
        await Expect(checkbox).ToBeCheckedAsync(new() { Checked = remember });
        await Expect(Page.GetByText("The value 'on' is not valid for 'RememberMe'.", new() { Exact = true }))
            .ToHaveCountAsync(0);
    }

    [Test]
    public async Task Register_InvalidFormWithoutJavaScript_ClearsBothPasswords()
    {
        await Page.GotoAsync(new Uri(application.EditorUri, "/account/register").AbsoluteUri);
        var password = Page.GetByLabel("Password", new() { Exact = true });
        var confirmation = Page.GetByLabel("Confirm password", new() { Exact = true });
        await password.FillAsync("Test-password-42!");
        await confirmation.FillAsync("Test-password-42!");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account", Exact = true }).ClickAsync();

        await Expect(Page.GetByText("The Email field is required.", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Expect(password).ToHaveValueAsync(string.Empty);
        await Expect(confirmation).ToHaveValueAsync(string.Empty);
    }
}
