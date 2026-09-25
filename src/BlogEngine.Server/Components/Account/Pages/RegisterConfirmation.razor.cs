using System.Text;

using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Email;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogEngine.Server.Components.Account.Pages;

public partial class RegisterConfirmation : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private IEmailSender<User> EmailSender { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;

    private string? _emailConfirmationLink;
    private string? _statusMessage;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // Only reachable after registering, and it reveals a confirmation link while the no-op email sender
        // is in use, so it's closed along with registration (design 12.1).
        if (!(await SettingsService.GetAsync()).AllowRegistration)
        {
            NavigationManager.NotFound();
            return;
        }

        if (Email is null)
        {
            RedirectManager.RedirectTo("");
            return;
        }

        var user = await UserManager.FindByEmailAsync(Email);
        if (user is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            _statusMessage = "Error finding user for unspecified email";
        }
        else if (EmailSender is IdentityEmailSender { IsDelivering: false })
        {
            // Without an SMTP server nobody receives the confirmation email, so show its link here instead.
            var userId = await UserManager.GetUserIdAsync(user);
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            _emailConfirmationLink = NavigationManager.GetUriWithQueryParameters(
                NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code, ["returnUrl"] = ReturnUrl });
        }
    }
}