namespace BlogEngine.Shared.Enums;

/// <summary>
/// Moderation state of a reader comment (design 6.5). Only <see cref="Approved"/> comments are shown publicly.
/// </summary>
/// <remarks>Values are persisted as integers and must never be renumbered.</remarks>
public enum CommentStatus
{
    /// <summary>Awaiting moderation.</summary>
    Pending = 0,

    /// <summary>Visible on the post page.</summary>
    Approved = 1,

    /// <summary>Hidden, but kept as a signal for future spam scoring.</summary>
    Rejected = 2,

    /// <summary>Flagged as spam by the spam guard or a moderator.</summary>
    Spam = 3
}