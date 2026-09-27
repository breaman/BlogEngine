namespace BlogEngine.Shared.Enums;

/// <summary>
/// What a comment block entry matches against (design 6.5, 8.3).
/// </summary>
/// <remarks>Values are persisted as integers and must never be renumbered.</remarks>
public enum CommentBlockKind
{
    /// <summary>A commenter's email address.</summary>
    Email = 0,

    /// <summary>A commenter's salted IP address hash.</summary>
    IpHash = 1,

    /// <summary>A word or phrase in the comment body.</summary>
    Keyword = 2,

    /// <summary>A domain in the commenter's email, website or links.</summary>
    Domain = 3
}