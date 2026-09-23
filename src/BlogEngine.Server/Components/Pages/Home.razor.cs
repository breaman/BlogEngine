using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages;

/// <summary>
/// The public home page at <c>/</c> (design 7.1, P3): featured posts pinned first, then the latest posts,
/// with a link to <c>/posts</c>.
/// </summary>
/// <remarks>
/// With the <see cref="HomePageMode.LatestPosts"/> setting the featured flag is ignored and only the latest
/// posts are shown. Featured posts aren't repeated in the latest list, which still shows "posts per page" posts.
/// </remarks>
public partial class Home : ComponentBase
{
    /// <summary>Most featured posts pinned above the latest posts.</summary>
    public const int MaxFeaturedPosts = 3;

    [Inject] private PublicPostQueries Queries { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;

    private string _siteTitle = SiteSettingsDefaults.SiteTitle;
    private string? _tagline;
    private string _dateFormat = SiteSettingsDefaults.DateFormat;
    private IReadOnlyList<PublicPostSummary> _featured = [];
    private IReadOnlyList<PublicPostSummary> _latest = [];

    /// <summary>Loads the settings, the featured posts and the latest posts that aren't featured above them.</summary>
    protected override async Task OnInitializedAsync()
    {
        var settings = await SettingsService.GetAsync();
        _siteTitle = settings.SiteTitle;
        _tagline = settings.Tagline;
        _dateFormat = settings.DateFormat;

        _featured = settings.HomePageMode == HomePageMode.FeaturedThenLatest
            ? await Queries.GetFeaturedAsync(MaxFeaturedPosts)
            : [];

        // Ask for enough posts to fill the list after removing the pinned ones.
        var latest = await Queries.GetLatestAsync(1, settings.PostsPerPage + _featured.Count);
        var pinned = _featured.Select(p => p.Id).ToHashSet();
        _latest = [.. latest.Posts.Where(p => !pinned.Contains(p.Id)).Take(settings.PostsPerPage)];
    }
}
