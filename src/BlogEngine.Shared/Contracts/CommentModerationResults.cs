namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Outcome of "Block commenter" (design 8.4): the email and IP blocks now in place and how many of the commenter's
/// comments were marked as spam.
/// </summary>
/// <param name="Blocks">The blocks covering the commenter, whether just added or already there.</param>
/// <param name="CommentsMarkedSpam">Comments by the commenter that were moved to spam.</param>
public sealed record CommenterBlocked(IReadOnlyList<CommentBlockDto> Blocks, int CommentsMarkedSpam);

/// <summary>
/// Outcome of adding a blocklist entry: exactly one of <see cref="CommentBlockSaved"/> or <see cref="CommentBlockInvalid"/>.
/// </summary>
public abstract record CommentBlockSaveResult;

/// <summary>The entry is in the blocklist (adding one that already exists returns the existing entry).</summary>
/// <param name="Block">The entry.</param>
public sealed record CommentBlockSaved(CommentBlockDto Block) : CommentBlockSaveResult;

/// <summary>The request failed validation and nothing was saved.</summary>
/// <param name="Errors">Messages keyed by property name.</param>
public sealed record CommentBlockInvalid(IReadOnlyDictionary<string, string[]> Errors) : CommentBlockSaveResult;

/// <summary>Response of <c>POST /api/admin/comments/bulk</c>.</summary>
/// <param name="Changed">How many comments were changed or deleted.</param>
public sealed record BulkModerationResponse(int Changed);

/// <summary>Response of <c>POST /api/admin/comments/empty-spam</c>.</summary>
/// <param name="Deleted">How many comments were deleted.</param>
public sealed record EmptySpamResponse(int Deleted);
