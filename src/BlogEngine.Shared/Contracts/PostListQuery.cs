using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Filters and paging for the admin posts list (<c>GET /api/admin/posts</c>). Every filter is optional and
/// they combine with AND.
/// </summary>
public sealed class PostListQuery
{
    /// <summary>Default page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Status tab.</summary>
    public PostListStatus Status { get; set; } = PostListStatus.All;

    /// <summary>Tag name (any casing) or tag slug.</summary>
    public string? Tag { get; set; }

    /// <summary>Text to find in the title, slug or summary.</summary>
    public string? Search { get; set; }

    /// <summary>1-based page number; values below 1 are treated as 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Posts per page, clamped to 1 through <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; set; } = DefaultPageSize;
}
