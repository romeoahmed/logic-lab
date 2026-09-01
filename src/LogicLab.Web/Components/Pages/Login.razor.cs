using System.ComponentModel.DataAnnotations;
using LogicLab.Infrastructure.Identity;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Login
{
    private string? errorMessage;

    [SupplyParameterFromForm]
    private LoginInput Input { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override void OnInitialized()
    {
        Input ??= new();
        _ = RedirectIfAuthenticated();
    }

    private async Task LogInAsync()
    {
        if (RedirectIfAuthenticated())
        {
            return;
        }

        var password = Input.Password;
        Input.Password = string.Empty;
        var result = await SignInManager.PasswordSignInAsync(
            Input.Email,
            password,
            Input.RememberMe,
            lockoutOnFailure: true);
        if (result.Succeeded)
        {
            RedirectManager.RedirectTo(ReturnUrl);
            return;
        }

        errorMessage = Text["LoginInvalid"];
    }

    private void ClearPassword() => Input.Password = string.Empty;

    private bool RedirectIfAuthenticated()
    {
        if (!SignInManager.IsSignedIn(HttpContext.User))
        {
            return false;
        }

        RedirectManager.RedirectTo(ReturnUrl);
        return true;
    }

    private sealed class LoginInput
    {
        [Required]
        [EmailAddress]
        [StringLength(AccountInputLimits.MaximumEmailLength)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(AccountInputLimits.MaximumPasswordLength)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
