using BlogEngine.Server.Services.Public;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// The table of contents shown above a long post's content (design 14.2, P13). Renders nothing for an empty outline,
/// which is what <see cref="PostOutline"/> returns for posts with fewer than four sections.
/// </summary>
public partial class TableOfContents : ComponentBase
{
    /// <summary>The post's outline.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<OutlineHeading> Entries { get; set; } = [];

    /// <summary>
    /// The page's own path. Links include it because the site's <c>&lt;base href="/"&gt;</c> would otherwise send a bare
    /// <c>#fragment</c> to the home page (see <see cref="FragmentLinks"/>).
    /// </summary>
    [Parameter, EditorRequired]
    public string PagePath { get; set; } = string.Empty;

    private string Link(OutlineHeading heading)
    {
        return $"{PagePath}#{Uri.EscapeDataString(heading.Id)}";
    }
}
