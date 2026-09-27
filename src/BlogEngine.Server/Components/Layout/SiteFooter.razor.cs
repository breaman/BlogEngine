using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Layout;

/// <summary>
/// The public site's footer: copyright line, the author's social links from the settings and the RSS feed.
/// </summary>
public partial class SiteFooter : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    private IReadOnlyList<SocialLinkDto> _socialLinks = [];
    private string _owner = string.Empty;
    private int _year;

    /// <summary>Loads the social links and the copyright owner (author, or else the site title) from the settings.</summary>
    protected override async Task OnInitializedAsync()
    {
        var settings = await SettingsService.GetAsync();
        _socialLinks = settings.SocialLinks;
        _owner = string.IsNullOrWhiteSpace(settings.AuthorName) ? settings.SiteTitle : settings.AuthorName;
        _year = TimeProvider.GetUtcNow().Year;
    }
}