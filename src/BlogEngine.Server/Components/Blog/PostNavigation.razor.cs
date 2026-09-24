using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// Links from a post to the posts published before and after it and to related posts (design 14.2, P10). Each part is
/// left out when there is nothing to show, such as the "newer" link on the newest post.
/// </summary>
/// <remarks>
/// The older post links with <c>rel="prev"</c> and the newer with <c>rel="next"</c>, reading the blog as a series from
/// its first post, the same direction as the list pager's "Older »".
/// </remarks>
public partial class PostNavigation : ComponentBase
{
    /// <summary>The posts before and after this one; see <see cref="PublicPostIndex.GetNeighbors"/>.</summary>
    [Parameter, EditorRequired]
    public PublicPostNeighbors Neighbors { get; set; } = PublicPostNeighbors.None;

    /// <summary>Posts sharing tags with this one; see <see cref="PublicPostIndex.GetRelated"/>.</summary>
    [Parameter]
    public IReadOnlyList<PublicPostSummary> Related { get; set; } = [];

    /// <summary>The site's date format setting, for the related posts' dates.</summary>
    [Parameter]
    public string DateFormat { get; set; } = SiteSettingsDefaults.DateFormat;
}
