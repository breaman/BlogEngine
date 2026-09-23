using System.Text.RegularExpressions;

using BlogEngine.Data.Models;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Server.Components.Account.Pages.Manage;

public partial class Index : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private User? _user;
    private string? _username;
    private string? _phoneNumber;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        _username = await UserManager.GetUserNameAsync(_user);
        _phoneNumber = await UserManager.GetPhoneNumberAsync(_user);

        Input.PhoneNumber ??= _phoneNumber;
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (Input.PhoneNumber != _phoneNumber)
        {
            var setPhoneResult = await UserManager.SetPhoneNumberAsync(_user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                RedirectManager.RedirectToCurrentPageWithStatus("Error: Failed to set phone number.", HttpContext);
                return;
            }
        }

        await SignInManager.RefreshSignInAsync(_user);
        RedirectManager.RedirectToCurrentPageWithStatus("Your profile has been updated", HttpContext);
    }

    private sealed class InputModel
    {
        public string? PhoneNumber { get; set; }
    }

    private sealed class InputModelValidator : AbstractValidator<InputModel>
    {
        /// <summary>
        /// Digits with optional spaces, dashes, dots and parentheses, an optional leading <c>+</c> and an optional
        /// extension (<c>x123</c> or <c>ext. 123</c>); the same shapes the old <c>[Phone]</c> attribute accepted.
        /// </summary>
        private const string PhoneNumberPattern = @"^\+?[\d\s\-.()]*\d[\d\s\-.()]*(\s*(x|ext\.?)\s*\d+)?$";

        public InputModelValidator()
        {
            // A blank number removes the phone number, so only a typed value has to match.
            RuleFor(x => x.PhoneNumber)
                .Matches(PhoneNumberPattern, RegexOptions.IgnoreCase).WithMessage("'{PropertyName}' is not a valid phone number.")
                .When(x => !string.IsNullOrEmpty(x.PhoneNumber))
                .WithName("Phone number");
        }
    }
}