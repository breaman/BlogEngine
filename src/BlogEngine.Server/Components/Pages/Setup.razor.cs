using BlogEngine.Data.Models;
using BlogEngine.Server.Components.Account;
using BlogEngine.Server.Services;
using BlogEngine.Shared.Common;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Server.Components.Pages;

/// <summary>
/// First-run page that creates the admin account (design 12.1, O5). Static SSR only; once any account
/// exists it answers 404, so it can't be used to create a second admin.
/// </summary>
public partial class Setup : ComponentBase
{
    [Inject] private AdminAccountService AdminAccounts { get; set; } = default!;
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private bool _alreadySetUp;
    private string? _errorMessage;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _alreadySetUp = await AdminAccounts.AnyUsersExistAsync();
        if (_alreadySetUp)
        {
            NavigationManager.NotFound();
        }
    }

    /// <summary>Creates the admin, signs them in and sends them to the dashboard.</summary>
    private async Task CreateAdminAsync()
    {
        // NotFound() above only changes the response; this guard makes sure a post can't create anything.
        // AdminAccountService re-checks under a lock for posts that race each other.
        if (_alreadySetUp)
        {
            return;
        }

        var (result, user) = await AdminAccounts.CreateInitialAdminAsync(Input.Email, Input.Password, Input.DisplayName);
        if (!result.Succeeded || user is null)
        {
            _errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return;
        }

        await SignInManager.SignInAsync(user, isPersistent: false);
        RedirectManager.RedirectTo("admin");
    }

    private sealed class InputModel
    {
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }

    private sealed class InputModelValidator : AbstractValidator<InputModel>
    {
        public InputModelValidator()
        {
            RuleFor(x => x.DisplayName)
                .NotEmpty()
                .MaximumLength(FieldLengths.PersonName)
                .WithName("Display name");

            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(FieldLengths.Email);

            RuleFor(x => x.Password)
                .NotEmpty()
                .Length(6, 100).WithMessage("The {PropertyName} must be at least {MinLength} and at max {MaxLength} characters long.");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.Password).WithMessage("The password and confirmation password do not match.")
                .WithName("Confirm password");
        }
    }
}
