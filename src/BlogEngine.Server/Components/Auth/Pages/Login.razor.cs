using System.Security.Claims;
using Blazored.FluentValidation;
using BlogEngine.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Server.Components.Auth.Pages;

public partial class Login : ComponentBase
{
    private FluentValidationValidator _fluentValidationValidator;
    private string? errorMessage;
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;
    [SupplyParameterFromForm]
    private LoginDto Dto { get; set; } = new();
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }
    [Inject]
    private ILogger<Login> Logger { get; set; }
    [Inject]
    private UserManager<User> UserManager { get; set; }
    [Inject]
    private SignInManager<User> SignInManager { get; set; }
    [Inject]
    private ApplicationDbContext DbContext { get; set; }
    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; }
    private async Task LoginUser()
    {
        if (await _fluentValidationValidator.ValidateAsync())
        {
            Logger.LogInformation("Attempting to sign in {User}", Dto.Email);
            var user = await UserManager.FindByEmailAsync(Dto.Email);
            if (user != null)
            {
                if (!await UserManager.IsEmailConfirmedAsync(user))
                {
                    errorMessage = "Your account has not been activated yet. Please activate your account first.";
                }
                else
                {
                    // this logic was borrowed from https://github.com/dotnet/aspnetcore/issues/46558
                    var isValid = await UserManager.CheckPasswordAsync(user, Dto.Password);
                    if (isValid)
                    {
                        var customClaims = new[]
                        {
                            new Claim(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
                            new Claim(ClaimTypes.Surname, user.LastName ?? string.Empty)
                        };
                        await SignInManager.SignInWithClaimsAsync(user, false, customClaims);
                        Logger.LogInformation("{User} logged in.", Dto.Email);
                        RedirectManager.RedirectTo(ReturnUrl);
                    }
                    else
                    {
                        errorMessage = "Invalid Username or Password.";
                    }
                }
            }
            else
            {
                errorMessage = "Invalid Username or Password";
            }
        }
    }
}