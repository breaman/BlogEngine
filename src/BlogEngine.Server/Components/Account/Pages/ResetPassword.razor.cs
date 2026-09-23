using System.Text;

using BlogEngine.Data.Models;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogEngine.Server.Components.Account.Pages;

public partial class ResetPassword : ComponentBase
{
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] private UserManager<User> UserManager { get; set; } = default!;

    private IEnumerable<IdentityError>? _identityErrors;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    private string? Message => _identityErrors is null ? null : $"Error: {string.Join(", ", _identityErrors.Select(error => error.Description))}";

    protected override void OnInitialized()
    {
        Input ??= new();

        if (Code is null)
        {
            RedirectManager.RedirectTo("Account/InvalidPasswordReset");
            return;
        }

        Input.Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
    }

    private async Task OnValidSubmitAsync()
    {
        var user = await UserManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal that the user does not exist
            RedirectManager.RedirectTo("Account/ResetPasswordConfirmation");
            return;
        }

        var result = await UserManager.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (result.Succeeded)
        {
            RedirectManager.RedirectTo("Account/ResetPasswordConfirmation");
            return;
        }

        _identityErrors = result.Errors;
    }

    private sealed class InputModel
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
        public string Code { get; set; } = "";
    }

    private sealed class InputModelValidator : AbstractValidator<InputModel>
    {
        public InputModelValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress();

            RuleFor(x => x.Password)
                .NotEmpty()
                .Length(6, 100).WithMessage("The {PropertyName} must be at least {MinLength} and at max {MaxLength} characters long.");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.Password).WithMessage("The password and confirmation password do not match.")
                .WithName("Confirm password");

            RuleFor(x => x.Code).NotEmpty();
        }
    }
}