using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// A pill-shaped link to a tag page, such as <c>#dotnet</c>, optionally with the tag's post count.
/// </summary>
public partial class TagBadge : ComponentBase
{
    /// <summary>The tag's display name.</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>The tag's URL slug.</summary>
    [Parameter, EditorRequired]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Number of posts with the tag, shown after the name when set (the tag index).</summary>
    [Parameter]
    public int? Count { get; set; }
}
