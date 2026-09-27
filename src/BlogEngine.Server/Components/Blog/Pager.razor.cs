using BlogEngine.Shared.Common;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// Newer/older links for a paginated post list. Lists are newest first, so "Newer" is the previous page.
/// Nothing is rendered for a list that fits on one page.
/// </summary>
public partial class Pager : ComponentBase
{
    /// <summary>The current 1-based page.</summary>
    [Parameter, EditorRequired]
    public int Page { get; set; }

    /// <summary>How many pages the list has.</summary>
    [Parameter, EditorRequired]
    public int TotalPages { get; set; }

    /// <summary>
    /// The list's path without a page number, such as <c>/posts</c>, <c>/tags/dotnet</c> or <c>/search?q=blazor</c>.
    /// </summary>
    [Parameter, EditorRequired]
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Label of the link to the previous page; lists are newest first, so it is "Newer" by default.</summary>
    [Parameter]
    public string PreviousText { get; set; } = "Newer";

    /// <summary>Label of the link to the next page; "Older" by default.</summary>
    [Parameter]
    public string NextText { get; set; } = "Older";

    /// <summary>The link to a page; page 1 has no <c>?page=</c>, so each page has one canonical URL.</summary>
    private string PageHref(int page)
    {
        return PostPaths.WithPage(BasePath, page);
    }
}