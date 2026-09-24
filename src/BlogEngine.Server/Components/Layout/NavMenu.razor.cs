using BlogEngine.Server.Services;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlogEngine.Server.Components.Layout;

/// <summary>
/// The public site's header: site title and tagline, the main sections and standalone pages, the search box, the
/// theme toggle and the account menu.
/// </summary>
public partial class NavMenu : ComponentBase
{
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private PublicPageQueries PageQueries { get; set; } = default!;

    private string FirstName { get; set; } = "";

    private bool _registrationAllowed;

    /// <summary>The site title from the settings (design 13), shown as the brand.</summary>
    private string _siteTitle = SiteSettingsDefaults.SiteTitle;

    /// <summary>The tagline from the settings, shown under the title.</summary>
    private string? _tagline;

    /// <summary>Published standalone pages shown in the navigation (design 6.7, A17).</summary>
    private IReadOnlyList<PublicPageSummary> _navPages = [];

    /// <summary>
    /// Loads the title, tagline and registration switch from the (cached) settings, the navigation pages, and the user's
    /// name.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        var settings = await SettingsService.GetAsync();
        _registrationAllowed = settings.AllowRegistration;
        _siteTitle = settings.SiteTitle;
        _tagline = settings.Tagline;
        _navPages = (await PageQueries.GetIndexAsync()).NavPages;

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
