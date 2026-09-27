using Microsoft.AspNetCore.Components;

namespace BlogEngine.Server.Components.Blog;

/// <summary>
/// A breadcrumb trail such as <c>Home › Posts › 2026 › September</c>, linking to the parent pages.
/// </summary>
public partial class Breadcrumbs : ComponentBase
{
    /// <summary>The trail from the home page to the current page; the last item is rendered as the current page.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<BreadcrumbItem> Items { get; set; } = [];
}

/// <summary>One step of a <see cref="Breadcrumbs"/> trail.</summary>
/// <param name="Text">The visible label.</param>
/// <param name="Href">The link target; <see langword="null"/> for the current page.</param>
public sealed record BreadcrumbItem(string Text, string? Href = null);