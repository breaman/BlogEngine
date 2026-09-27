using System.ComponentModel;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>An approved comment as the post page shows it (design 14.2, T3.6). It never carries the email.</summary>
/// <param name="Id">Comment id, for the <c>#comment-{id}</c> anchor.</param>
/// <param name="ParentCommentId">The comment it replies to, if any.</param>
/// <param name="AuthorName">Commenter display name.</param>
/// <param name="AuthorUrl">Commenter website, if given.</param>
/// <param name="BodyHtml">Sanitized comment HTML.</param>
/// <param name="CreatedOn">When it was submitted.</param>
/// <param name="IsAuthorReply">Whether the blog author wrote it.</param>
/// <param name="AvatarHash">
/// SHA-256 of the trimmed, lowercased email: the Gravatar id when avatars are on, and the seed of the initial's color
/// when they are off. It is only rendered into the page when avatars are on.
/// </param>
[ImmutableObject(true)]
public sealed record PublicComment(
    int Id,
    int? ParentCommentId,
    string AuthorName,
    string? AuthorUrl,
    string BodyHtml,
    DateTimeOffset CreatedOn,
    bool IsAuthorReply,
    string AvatarHash);

/// <summary>
/// The approved comments of one post, oldest first. A dedicated type rather than a bare list, because HybridCache
/// only reuses cached instances of sealed immutable types.
/// </summary>
/// <param name="Comments">The comments.</param>
[ImmutableObject(true)]
public sealed record PublicCommentList(IReadOnlyList<PublicComment> Comments)
{
    /// <summary>A post with no approved comments.</summary>
    public static PublicCommentList Empty { get; } = new([]);
}