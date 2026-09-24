using System.Globalization;

using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// The SEO head of a public page (design 14.2, 16, P8): the browser title, meta description, canonical URL, Open Graph
/// and Twitter card tags, the JSON-LD <c>BlogPosting</c> of a post, and feed autodiscovery links.
/// </summary>
/// <remarks>
/// <para>
/// Canonical URLs are absolute and built from the canonical paths (<see cref="PostPaths"/>, <see cref="TagPaths"/>),
/// so they are lowercase, zero-padded, have no trailing slash and omit <c>?page=1</c>. The sitewide
/// <c>noindex</c> for "discourage search engines" is written by <c>App</c> instead, so it covers every page.
/// </para>
/// <para>
/// The social image is the page's own (a post's social image or cover), else the default social image from the
/// settings, else none, in which case Twitter gets a small <c>summary</c> card instead of a large image.
/// </para>
/// </remarks>
public partial class SeoHead : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private PublicPostQueries Queries { get; set; } = default!;
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

    /// <summary>Describes the page as a blog post: <c>og:type=article</c>, <c>article:*</c> tags and JSON-LD.</summary>
    [Parameter]
    public SeoArticle? Article { get; set; }

    /// <summary>Asks search engines not to index this page (search results), while still following its links.</summary>
    [Parameter]
    public bool NoIndex { get; set; }

    private string _siteTitle = SiteSettingsDefaults.SiteTitle;
    private string _fullTitle = SiteSettingsDefaults.SiteTitle;
    private string _socialTitle = SiteSettingsDefaults.SiteTitle;
    private string? _description;
    private string _canonicalUrl = string.Empty;
    private PublicImage? _image;
    private string? _imageUrl;
    private string? _jsonLd;
    private bool _noIndex;

    /// <summary>Combines the page's values with the site title, description and default image from the (cached) settings.</summary>
    protected override async Task OnParametersSetAsync()
    {
        var settings = await SettingsService.GetAsync();
        _siteTitle = settings.SiteTitle;
        _fullTitle = string.IsNullOrWhiteSpace(Title) ? settings.SiteTitle : $"{Title} | {settings.SiteTitle}";
        _socialTitle = string.IsNullOrWhiteSpace(Title) ? settings.SiteTitle : Title;
        _description = string.IsNullOrWhiteSpace(Description)
            ? settings.Description ?? settings.Tagline
            : Description;
        _canonicalUrl = Absolute(CanonicalPath);

        // App already writes "noindex, nofollow" on every page when search engines are discouraged.
        _noIndex = NoIndex && !settings.DiscourageSearchEngines;

        _image = Image ?? (settings.DefaultSocialImageMediaId is { } defaultImageId
            ? await Queries.GetMediaImageAsync(defaultImageId)
            : null);
        _imageUrl = _image is null ? null : Absolute(_image.Url);

        _jsonLd = Article is null
            ? null
            : BlogPostingSchema.ToJson(new BlogPostingSchema.Data(new Uri(_canonicalUrl), Article, _description, _imageUrl,
                settings.AuthorName, settings.SiteTitle, new Uri(Absolute(SitePaths.Home))));
    }

    /// <summary>An absolute URL on this site for <paramref name="path"/>.</summary>
    private string Absolute(string path)
    {
        return NavigationManager.ToAbsoluteUri(path).AbsoluteUri;
    }

    /// <summary>An ISO 8601 UTC timestamp for the <c>article:*</c> times.</summary>
    private static string Iso(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
