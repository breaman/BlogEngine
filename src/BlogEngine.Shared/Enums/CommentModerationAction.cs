namespace BlogEngine.Shared.Enums;

/// <summary>
/// An action from the moderation queue (design 8.4), applied to one comment or in bulk.
/// </summary>
public enum CommentModerationAction
{
    /// <summary>Show the comment on the site.</summary>
    Approve,

    /// <summary>Hide it, keeping it as a signal for future spam scoring.</summary>
    Reject,

    /// <summary>Flag it as spam.</summary>
    Spam,

    /// <summary>Delete it permanently, with its replies.</summary>
    Delete
}