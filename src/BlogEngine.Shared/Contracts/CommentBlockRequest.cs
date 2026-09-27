using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A new blocklist entry (<c>POST /api/admin/comment-blocks</c>, design 7.4, T3.9).
/// </summary>
public sealed class CommentBlockRequest
{
    /// <summary>What <see cref="Value"/> is matched against; keywords by default.</summary>
    public CommentBlockKind Kind { get; set; } = CommentBlockKind.Keyword;

    /// <summary>The email, IP hash, keyword or domain to block; matched case-insensitively.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional note explaining the block.</summary>
    public string? Note { get; set; }
}