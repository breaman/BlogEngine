namespace BlogEngine.Shared.Contracts;

/// <summary>
/// How many comments are in each moderation tab (design 8.4), for the tab labels and the admin nav badge.
/// </summary>
public sealed class CommentStatusCounts
{
    /// <summary>Comments awaiting moderation.</summary>
    public int Pending { get; set; }

    /// <summary>Comments shown on the site.</summary>
    public int Approved { get; set; }

    /// <summary>Comments rejected by the moderator.</summary>
    public int Rejected { get; set; }

    /// <summary>Comments flagged as spam.</summary>
    public int Spam { get; set; }
}
