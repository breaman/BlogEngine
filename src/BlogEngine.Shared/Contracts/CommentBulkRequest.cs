using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One moderation action applied to several comments at once (<c>POST /api/admin/comments/bulk</c>, design 8.4).
/// </summary>
public sealed class CommentBulkRequest
{
    /// <summary>Most comments one request may change.</summary>
    public const int MaxIds = 200;

    /// <summary>The comments to change.</summary>
    public List<int> Ids { get; set; } = [];

    /// <summary>What to do with them.</summary>
    public CommentModerationAction Action { get; set; }
}
