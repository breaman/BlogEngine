using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// Site search at <c>/search?q=</c> (design 15, P9): visible posts containing every word of the search, with title
/// matches first, paginated with <c>&amp;page=</c>.
/// </summary>
/// <remarks>
/// <para>
/// Static SSR with a GET form, so it works without JavaScript and results can be bookmarked. Result pages carry
/// <c>noindex</c>: search engines should index the posts, not endless combinations of search words.
/// </para>
/// <para>
/// Rate limited per client (<see cref="SearchRateLimiting"/>), since every search queries the database. A page number
/// past the last page is a 404, like the other lists.
/// </para>
/// </remarks>
[EnableRateLimiting(SearchRateLimiting.PolicyName)]
public partial class Search : ComponentBase
{
    [Inject] private PublicSearchQueries SearchQueries { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The search as typed.</summary>
    [SupplyParameterFromQuery(Name = "q")]
    private string? QueryText { get; set; }

    /// <summary>The <c>page=</c> value, parsed by <see cref="PageNumber"/>.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    private string? PageText { get; set; }

    private SearchQuery? _query;
    private PublicPostPage? _page;
    private string _title = "Search";
    private string _basePath = SitePaths.Search;
    private string _canonicalPath = SitePaths.Search;
    private string _dateFormat = SiteSettingsDefaults.DateFormat;

    /// <summary>Runs the search, or shows just the form when there is nothing to search for.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _page = null;
        _query = SearchQuery.Parse(QueryText);
        if (!PageNumber.TryParse(PageText, out var pageNumber))
        {
            NavigationManager.NotFound();
            return;
        }

        if (_query is null)
        {
            _title = "Search";
            _basePath = _canonicalPath = SitePaths.Search;
            return;
        }

        var settings = await SettingsService.GetAsync();
        _dateFormat = settings.DateFormat;

        var page = await SearchQueries.SearchAsync(_query, pageNumber, settings.PostsPerPage);
        if (!page.IsPageInRange)
        {
            NavigationManager.NotFound();
            return;
        }

        _page = page;
        _basePath = $"{SitePaths.Search}?q={Uri.EscapeDataString(_query.Text)}";
        _canonicalPath = PostPaths.WithPage(_basePath, page.Page);
        _title = page.Page > 1 ? $"Search: {_query.Text}, page {page.Page}" : $"Search: {_query.Text}";
    }
}