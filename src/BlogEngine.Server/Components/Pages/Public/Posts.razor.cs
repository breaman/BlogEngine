using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// The post index at <c>/posts</c> (design 7.1, P3): every visible post, newest first, paginated with
/// <c>?page=N</c>. Pages past the end, and malformed page numbers, answer 404.
/// </summary>
public partial class Posts : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The <c>?page=</c> value, parsed by <see cref="PageNumber"/>.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    private string? PageText { get; set; }

    private PublicPostPage? _page;
    private string _dateFormat = SiteSettingsDefaults.DateFormat;

    /// <summary>Loads the requested page, or answers 404 when it doesn't exist.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (!PageNumber.TryParse(PageText, out var pageNumber))
        {
            NavigationManager.NotFound();
            return;
        }

        var settings = await SettingsService.GetAsync();
        _dateFormat = settings.DateFormat;

        var page = await Queries.GetLatestAsync(pageNumber, settings.PostsPerPage);
        if (!page.IsPageInRange)
        {
            NavigationManager.NotFound();
            return;
        }

        _page = page;
    }
}