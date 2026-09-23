using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// A post in a list: its linked title, date, reading time, tags and summary.
/// </summary>
public partial class PostCard : ComponentBase
{
    /// <summary>The post.</summary>
    [Parameter, EditorRequired]
    public PublicPostSummary Post { get; set; } = default!;

    /// <summary>The site's date format setting.</summary>
    [Parameter]
    public string DateFormat { get; set; } = SiteSettingsDefaults.DateFormat;

    /// <summary>Marks featured posts with a badge (the home page does).</summary>
    [Parameter]
    public bool ShowFeatured { get; set; }
}
