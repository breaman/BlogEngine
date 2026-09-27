namespace BlogEngine.Shared.Enums;

/// <summary>
/// How much of each post the RSS and Atom feeds include (design 13).
/// </summary>
/// <remarks>Values are persisted as integers and must never be renumbered.</remarks>
public enum FeedContentMode
{
    /// <summary>The full rendered post.</summary>
    FullContent = 0,

    /// <summary>Only the post summary, with a link to the post.</summary>
    Summary = 1
}