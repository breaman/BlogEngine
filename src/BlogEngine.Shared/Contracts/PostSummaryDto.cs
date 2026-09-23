using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One row of the admin posts list (design 7.3, O2).
/// </summary>
public sealed class PostSummaryDto
{
    /// <summary>The post id.</summary>
    public int Id { get; set; }

    /// <summary>Post title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>URL slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Draft or published.</summary>
    public PostStatus Status { get; set; }

    /// <summary>When the post was published (UTC), or <see langword="null"/> if it never was.</summary>
    public DateTimeOffset? PublishedOn { get; set; }

    /// <summary>When the post was last saved.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Tag names, alphabetically.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Readable words in the content.</summary>
    public int WordCount { get; set; }

    /// <summary>Whether the post is pinned to the home page.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>The public URL path while the post is published, otherwise <see langword="null"/>.</summary>
    public string? PublicPath { get; set; }

    /// <summary>Concurrency token, so row actions such as unpublish can detect a stale list.</summary>
    public byte[]? RowVersion { get; set; }
}
