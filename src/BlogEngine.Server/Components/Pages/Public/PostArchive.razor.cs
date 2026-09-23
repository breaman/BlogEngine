using System.Globalization;

using BlogEngine.Server.Components.Blog;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// The year, month and day archives (design 7.1, P2, P4): <c>/posts/{yyyy}</c>, <c>/posts/{yyyy}/{mm}</c>
/// and <c>/posts/{yyyy}/{mm}/{dd}</c>, using the same list as <c>/posts</c>.
/// </summary>
/// <remarks>
/// Months and days are accepted with or without a leading zero (<c>9</c> or <c>09</c>), but every link and the
/// canonical URL use two digits. An impossible date (<c>/posts/2026/02/30</c>) and a real period with no visible
/// posts both answer 404, so search engines never index empty archive pages.
/// </remarks>
public partial class PostArchive : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>The year from the URL.</summary>
    [Parameter]
    public int Year { get; set; }

    /// <summary>The month from the URL, if the route has one.</summary>
    [Parameter]
    public int? Month { get; set; }

    /// <summary>The day from the URL, if the route has one.</summary>
    [Parameter]
    public int? Day { get; set; }

    /// <summary>The <c>?page=</c> value, parsed by <see cref="PageNumber"/>.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    private string? PageText { get; set; }

    private ArchivePeriod? _period;
    private PublicPostPage? _page;
    private string _heading = string.Empty;
    private IReadOnlyList<BreadcrumbItem> _breadcrumbs = [];
    private string _dateFormat = SiteSettingsDefaults.DateFormat;

    /// <summary>Validates the date, loads the period's posts, and answers 404 for an empty or impossible period.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _page = null;
        _period = ArchivePeriod.TryCreate(Year, Month, Day);
        if (_period is null || !PageNumber.TryParse(PageText, out var pageNumber))
        {
            NavigationManager.NotFound();
            return;
        }

        var settings = await SettingsService.GetAsync();
        _dateFormat = settings.DateFormat;

        var page = await Queries.GetArchiveAsync(_period, pageNumber, settings.PostsPerPage);
        if (page.TotalCount == 0 || !page.IsPageInRange)
        {
            NavigationManager.NotFound();
            return;
        }

        _page = page;
        _heading = $"Posts from {Describe(_period, _dateFormat)}";
        _breadcrumbs = BuildBreadcrumbs(_period);
    }

    /// <summary>The period in words: "2026", "September 2026", or the day in the site's date format.</summary>
    private static string Describe(ArchivePeriod period, string dateFormat)
    {
        return (period.Month, period.Day) switch
        {
            (null, _) => period.Year.ToString(CultureInfo.InvariantCulture),
            ({ } month, null) => PublicDates.MonthYear(period.Year, month),
            _ => PublicDates.Format(period.Start, dateFormat)
        };
    }

    /// <summary>Home › Posts › year › month › day, each linking to its archive except the current one.</summary>
    private static List<BreadcrumbItem> BuildBreadcrumbs(ArchivePeriod period)
    {
        List<BreadcrumbItem> items = [new("Home", SitePaths.Home), new("Posts", PostPaths.Index)];

        items.Add(new(period.Year.ToString(CultureInfo.InvariantCulture), PostPaths.Year(period.Year)));
        if (period.Month is { } month)
        {
            items.Add(new(PublicDates.MonthName(month), PostPaths.Month(period.Year, month)));
        }

        if (period.Day is { } day)
        {
            items.Add(new(day.ToString(CultureInfo.InvariantCulture), period.Path));
        }

        return items;
    }
}
