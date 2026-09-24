namespace BlogEngine.Shared.Contracts;

/// <summary>
/// What the admin dashboard shows (design 4.5 O1, T3.10): post counts, the moderation queue size and recent activity.
/// </summary>
public sealed class DashboardSummaryDto
{
    /// <summary>Posts that are drafts (outside the trash).</summary>
    public int DraftCount { get; set; }

    /// <summary>Published posts whose publish time has passed.</summary>
    public int PublishedCount { get; set; }

    /// <summary>Published posts whose publish time is still in the future.</summary>
    public int ScheduledCount { get; set; }

    /// <summary>Comments awaiting moderation.</summary>
    public int PendingCommentCount { get; set; }

    /// <summary>The most recently changed posts, newest first.</summary>
    public List<PostSummaryDto> RecentPosts { get; set; } = [];

    /// <summary>The most recent comments of any status, newest first.</summary>
    public List<CommentDto> RecentComments { get; set; } = [];
}
