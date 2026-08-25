using System.ComponentModel.DataAnnotations;
using LogicLab.Web.Data;
using LogicLab.Web.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Register
{
    private IEnumerable<IdentityError>? errors;

    [SupplyParameterFromForm]
    private RegistrationInput Input { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override void OnInitialized()
    {
        Input ??= new();
        _ = RedirectIfAuthenticated();
    }

    private async Task RegisterAsync()
    {
        if (RedirectIfAuthenticated())
        {
            return;
        }

        var password = Input.Password;
        ClearPasswords();
        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
        };
        var result = await UserManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            errors = result.Errors;
            return;
        }

        await SignInManager.SignInAsync(user, isPersistent: false);
        RedirectManager.RedirectTo(ReturnUrl);
    }

    private void ClearPasswords()
    {
        Input.Password = string.Empty;
        Input.ConfirmPassword = string.Empty;
    }

    private bool RedirectIfAuthenticated()
    {
        if (!SignInManager.IsSignedIn(HttpContext.User))
        {
            return false;
        }

        RedirectManager.RedirectTo(ReturnUrl);
        return true;
    }

    private sealed class RegistrationInput
    {
        [Required]
        [EmailAddress]
        [StringLength(AccountInputLimits.MaximumEmailLength)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(
            AccountInputLimits.MaximumPasswordLength,
            MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(AccountInputLimits.MaximumPasswordLength)]
        [Compare(nameof(Password))]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
