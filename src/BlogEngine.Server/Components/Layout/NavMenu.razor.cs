using BlogEngine.Server.Services;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlogEngine.Server.Components.Layout;

public partial class NavMenu : ComponentBase
{
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [Inject] private ISettingsService SettingsService { get; set; } = default!;

    private string FirstName { get; set; } = "";

    private bool _registrationAllowed;

    protected override async Task OnInitializedAsync()
    {
        _registrationAllowed = (await SettingsService.GetAsync()).AllowRegistration;

        if (AuthenticationStateTask is not null)
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                FirstName = user.FindFirst(CustomUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value
                    ?? user.FindFirst(CustomUserClaimsPrincipalFactory.FirstNameClaimType)?.Value
                    ?? user.Identity.Name
                    ?? "";
            }
        }
    }
}
