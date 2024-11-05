using System.Text;
using BlogEngine.Server.Components.Email;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogEngine.Server.Components.Auth.Pages;

public partial class RegisterConfirmation : ComponentBase
{
    private string? emailConfirmationLink;
    private string? statusMessage;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    
    [SupplyParameterFromQuery]
    private string? Email { get; set; }
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private IEnhancedEmailSender<User> EmailSender { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (Email is null)
        {
            RedirectManager.RedirectTo("");
        }

        var user = await UserManager.FindByEmailAsync(Email);
        if (user is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            statusMessage = "Error finding user for unspecified email";
        }
        else if (EmailSender is IdentityNoOpEmailSender)
        {
            // once you add a real emails ender, you should remove this code that lets you confirm the account
            var userId = await UserManager.GetUserIdAsync(user);
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            emailConfirmationLink = NavigationManager.GetUriWithQueryParameters(
                NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["email"] = user.Email, ["code"] = code, ["returnUrl"] = ReturnUrl });
        }
    }
}