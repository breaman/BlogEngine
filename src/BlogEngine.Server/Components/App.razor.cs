using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components;

/// <summary>
/// The root component: the HTML document shared by every page, public and admin.
/// </summary>
public partial class App : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private PublicPostQueries Queries { get; set; } = default!;

    /// <summary>Whether to ask search engines not to index any page (design 13).</summary>
    private bool _discourageSearchEngines;

    /// <summary>The favicon chosen in the settings (design 13), or <see langword="null"/> for the built-in one.</summary>
    private string? _faviconUrl;

    /// <summary>Reads the "discourage search engines" switch and the favicon from the (cached) settings.</summary>
    protected override async Task OnInitializedAsync()
    {
        var settings = await SettingsService.GetAsync();
        _discourageSearchEngines = settings.DiscourageSearchEngines;

        // Cached with the other public images; the versioned URL changes when the image is edited, so browsers refetch it.
        _faviconUrl = settings.FaviconMediaId is { } faviconId ? (await Queries.GetMediaImageAsync(faviconId))?.Url : null;
    }
}