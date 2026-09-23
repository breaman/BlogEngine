using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// A tag page at <c>/tags/{slug}</c> (design 7.1, P5): the tag's visible posts, newest first, paginated like
/// <c>/posts</c>. A tag with no visible posts (only drafts, or none at all) answers 404.
/// </summary>
public partial class TagDetail : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The tag slug from the URL.</summary>
    [Parameter]
    public string Slug { get; set; } = string.Empty;

    /// <summary>The <c>?page=</c> value, parsed by <see cref="PageNumber"/>.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    private string? PageText { get; set; }

    private PublicTag? _tag;
    private PublicPostPage? _page;
    private string _dateFormat = SiteSettingsDefaults.DateFormat;

    /// <summary>Loads the tag and the requested page of its posts, or answers 404.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _tag = null;
        _page = null;

        var tag = await Queries.GetTagAsync(Slug);
        if (tag is null || !PageNumber.TryParse(PageText, out var pageNumber))
        {
            NavigationManager.NotFound();
            return;
        }

        var settings = await SettingsService.GetAsync();
        _dateFormat = settings.DateFormat;

        var page = await Queries.GetTaggedAsync(tag.Id, pageNumber, settings.PostsPerPage);
        if (!page.IsPageInRange)
        {
            NavigationManager.NotFound();
            return;
        }

        _tag = tag;
        _page = page;
    }
}
