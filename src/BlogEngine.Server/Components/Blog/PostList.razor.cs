using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// One page of posts with a <see cref="Pager"/>, used by the post index, the date archives, tag pages and search.
/// </summary>
public partial class PostList : ComponentBase
{
    /// <summary>The page of posts to show.</summary>
    [Parameter, EditorRequired]
    public PublicPostPage Page { get; set; } = default!;

    /// <summary>The list's path without a page number, used for the pager links.</summary>
    [Parameter, EditorRequired]
    public string BasePath { get; set; } = string.Empty;

    /// <summary>The site's date format setting.</summary>
    [Parameter]
    public string DateFormat { get; set; } = SiteSettingsDefaults.DateFormat;

    /// <summary>Label of the pager's link to the previous page (see <see cref="Pager.PreviousText"/>).</summary>
    [Parameter]
    public string PreviousText { get; set; } = "Newer";

    /// <summary>Label of the pager's link to the next page (see <see cref="Pager.NextText"/>).</summary>
    [Parameter]
    public string NextText { get; set; } = "Older";

    /// <summary>Text shown when there are no posts.</summary>
    [Parameter]
    public string EmptyText { get; set; } = "No posts yet.";
}