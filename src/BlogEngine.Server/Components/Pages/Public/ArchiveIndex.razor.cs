using System.Globalization;

using BlogEngine.Server.Services.Public;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Pages.Public;

/// <summary>
/// The archive overview at <c>/archive</c> (design 7.1, P11): every year with posts, newest first, with its months and
/// their post counts, each linking to its year or month archive.
/// </summary>
/// <remarks>
/// Computed from the same cached snapshot as the archive pages (<see cref="ArchiveOverview"/>), so every count matches
/// the page it links to. An empty blog gets a 200 with "No posts yet." rather than a 404: the page itself always exists.
/// </remarks>
public partial class ArchiveIndex : ComponentBase
{
    [Inject] private PublicPostQueries Queries { get; set; } = default!;

    private IReadOnlyList<ArchiveYear> _years = [];
    private int _postCount;

    /// <summary>Loads the years and months with visible posts.</summary>
    protected override async Task OnInitializedAsync()
    {
        _years = await Queries.GetArchiveOverviewAsync();
        _postCount = _years.Sum(y => y.PostCount);
    }

    /// <summary>"1 post" or "12 posts".</summary>
    private static string Count(int posts)
    {
        return posts == 1 ? "1 post" : string.Create(CultureInfo.InvariantCulture, $"{posts:N0} posts");
    }
}