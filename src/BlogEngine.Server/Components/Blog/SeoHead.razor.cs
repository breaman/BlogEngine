using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// The basic SEO head of a public page (design 14.2, 16): the browser title, meta description, canonical URL,
/// social sharing image and feed autodiscovery links.
/// </summary>
/// <remarks>
/// Canonical URLs are absolute and built from the canonical paths (<see cref="PostPaths"/>, <see cref="TagPaths"/>),
/// so they are lowercase, zero-padded, have no trailing slash and omit <c>?page=1</c>. The sitewide
/// <c>noindex</c> for "discourage search engines" is written by <c>App</c> instead, so it covers every page.
/// </remarks>
public partial class SeoHead : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The page's own title; the site title is appended. Leave empty on the home page.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>The meta description; the site description is used when this is empty.</summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>The page's canonical path and query string, such as <c>/posts?page=2</c>.</summary>
    [Parameter, EditorRequired]
    public string CanonicalPath { get; set; } = SitePaths.Home;

    /// <summary>The image for social sharing previews (Open Graph and Twitter cards), such as a post's cover.</summary>
    [Parameter]
    public PublicImage? Image { get; set; }

    /// <summary>A tag whose feed is advertised alongside the site feeds (tag pages).</summary>
    [Parameter]
    public PublicTag? TagFeed { get; set; }

    private string _siteTitle = SiteSettingsDefaults.SiteTitle;
    private string _fullTitle = SiteSettingsDefaults.SiteTitle;
    private string? _description;

    /// <summary>Combines the page's values with the site title and description from the (cached) settings.</summary>
    protected override async Task OnParametersSetAsync()
    {
        var settings = await SettingsService.GetAsync();
        _siteTitle = settings.SiteTitle;
        _fullTitle = string.IsNullOrWhiteSpace(Title) ? settings.SiteTitle : $"{Title} | {settings.SiteTitle}";
        _description = string.IsNullOrWhiteSpace(Description)
            ? settings.Description ?? settings.Tagline
            : Description;
    }

    /// <summary>An absolute URL on this site for <paramref name="path"/>.</summary>
    private string Absolute(string path)
    {
        return NavigationManager.ToAbsoluteUri(path).AbsoluteUri;
    }
}
