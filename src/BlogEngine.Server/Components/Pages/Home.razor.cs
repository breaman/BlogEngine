using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages;

/// <summary>
/// The public home page at <c>/</c>. Shows the site title and tagline from the settings until the post lists
/// arrive (T1.19); the settings are cached and evicted on save, so edits show on the next request.
/// </summary>
public partial class Home : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;

    private string _siteTitle = SiteSettingsDefaults.SiteTitle;
    private string? _tagline;

    /// <summary>Reads the title and tagline from the (cached) settings.</summary>
    protected override async Task OnInitializedAsync()
    {
        var settings = await SettingsService.GetAsync();
        _siteTitle = settings.SiteTitle;
        _tagline = settings.Tagline;
    }
}
