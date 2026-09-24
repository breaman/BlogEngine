using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Filters and paging for the moderation queue (<c>GET /api/admin/comments?status=</c>, design 7.4, 8.4).
/// </summary>
public sealed class CommentListQuery
{
    /// <summary>Default page size.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 100;

    /// <summary>The queue tab; pending comments by default.</summary>
    public CommentStatus Status { get; set; } = CommentStatus.Pending;

    /// <summary>1-based page number; values below 1 are treated as 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Comments per page, clamped to 1 through <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; set; } = DefaultPageSize;
}
