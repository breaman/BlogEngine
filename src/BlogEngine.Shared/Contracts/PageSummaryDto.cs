using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>One row of the admin pages list (design 7.3, A17).</summary>
public sealed class PageSummaryDto
{
    /// <summary>The page id.</summary>
    public int Id { get; set; }

    /// <summary>Page title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>URL slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Draft or published.</summary>
    public PostStatus Status { get; set; }

    /// <summary>Whether the page is linked from the site navigation when published.</summary>
    public bool ShowInNav { get; set; }

    /// <summary>Position in the navigation; lower values come first.</summary>
    public int NavOrder { get; set; }

    /// <summary>When the page was last saved.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>The public URL path while the page is published, otherwise <see langword="null"/>.</summary>
    public string? PublicPath { get; set; }
}