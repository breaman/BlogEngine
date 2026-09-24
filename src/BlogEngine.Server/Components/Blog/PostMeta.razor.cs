using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// A post's publish date, reading time and tags, shown under its title on list pages and the post page.
/// </summary>
public partial class PostMeta : ComponentBase
{
    /// <summary>The post.</summary>
    [Parameter, EditorRequired]
    public PublicPostSummary Post { get; set; } = default!;

    /// <summary>The site's date format setting.</summary>
    [Parameter]
    public string DateFormat { get; set; } = SiteSettingsDefaults.DateFormat;

    /// <summary>Links the date to its day archive (the post page does, design 14.2).</summary>
    [Parameter]
    public bool LinkDate { get; set; }

    /// <summary>
    /// The "Updated" date shown after the publish date (P15), in the blog's time zone; see
    /// <see cref="PublicPostSummary.UpdatedDateLocal"/>. Nothing is shown when <see langword="null"/>.
    /// </summary>
    [Parameter]
    public DateOnly? UpdatedDate { get; set; }

    /// <summary>Whether to list the tags.</summary>
    [Parameter]
    public bool ShowTags { get; set; } = true;

    /// <summary>Extra CSS classes, such as a margin.</summary>
    [Parameter]
    public string? Class { get; set; }
}
