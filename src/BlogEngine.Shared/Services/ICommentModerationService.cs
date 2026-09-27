using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Services;

/// <summary>
/// The comment moderation queue and blocklist for the admin area (design 5.1, 7.4, 8.4).
/// </summary>
/// <remarks>
/// The server implementation works on the database directly (used while prerendering and by the admin API); the
/// WebAssembly implementation calls <c>/api/admin/comments</c> and <c>/api/admin/comment-blocks</c>. Every change that
/// could alter what readers see evicts that post's cached comments.
/// </remarks>
public interface ICommentModerationService
{
    /// <summary>Lists one moderation tab, newest first.</summary>
    Task<PagedResult<CommentDto>> GetCommentsAsync(CommentListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Counts the comments in every moderation tab.</summary>
    Task<CommentStatusCounts> GetCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves, rejects or flags one comment as spam, or deletes it (with its replies) for
    /// <see cref="CommentModerationAction.Delete"/>.
    /// </summary>
    /// <returns><see langword="false"/> if there is no such comment.</returns>
    Task<bool> ModerateAsync(int id, CommentModerationAction action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the author's reply to a comment (design 8.4, C5): approved straight away, marked as the author's, and
    /// filed under the top-level comment (threads are one level deep). Replying approves the comment replied to, and
    /// its top-level comment when that is still waiting.
    /// </summary>
    Task<CommentReplyResult> ReplyAsync(int id, CommentReplyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Applies one action to several comments; ids that don't exist are skipped.</summary>
    /// <returns>How many comments were changed or deleted.</returns>
    Task<int> ModerateManyAsync(CommentBulkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocks the commenter who wrote the comment: adds email and IP-hash blocks and marks all of their comments as
    /// spam (design 8.4). Returns <see langword="null"/> if there is no such comment.
    /// </summary>
    Task<CommenterBlocked?> BlockCommenterAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes spam older than 30 days (design 8.4).</summary>
    /// <returns>How many comments were deleted.</returns>
    Task<int> EmptySpamAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the blocklist, grouped by kind.</summary>
    Task<IReadOnlyList<CommentBlockDto>> GetBlocksAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a blocklist entry, or returns the existing one with the same kind and value.</summary>
    Task<CommentBlockSaveResult> AddBlockAsync(CommentBlockRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a blocklist entry.</summary>
    /// <returns><see langword="false"/> if there is no such entry.</returns>
    Task<bool> DeleteBlockAsync(int id, CancellationToken cancellationToken = default);
}