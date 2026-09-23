namespace BlogEngine.Shared.Enums;

/// <summary>
/// Publication state of a post or standalone page (design 6.2, 6.3).
/// </summary>
/// <remarks>
/// "Scheduled" is deliberately not a status: it is <see cref="Published"/> with a future publish date,
/// so the public visibility rule alone decides when a post goes live. Values are persisted as integers
/// and referenced by a filtered index, so they must never be renumbered.
/// </remarks>
public enum PostStatus
{
    /// <summary>Work in progress; never publicly visible.</summary>
    Draft = 0,

    /// <summary>Published now, or scheduled when the publish date is in the future.</summary>
    Published = 1
}
