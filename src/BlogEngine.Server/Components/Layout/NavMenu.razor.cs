using BlogEngine.Server.Services;
using BlogEngine.Shared.Common;
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

    /// <summary>The site title from the settings (design 13), shown as the brand.</summary>
    private string _siteTitle = SiteSettingsDefaults.SiteTitle;

    protected override async Task OnInitializedAsync()
    {
        var settings = await SettingsService.GetAsync();
        _registrationAllowed = settings.AllowRegistration;
        _siteTitle = settings.SiteTitle;

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
