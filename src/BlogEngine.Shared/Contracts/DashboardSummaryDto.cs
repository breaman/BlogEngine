namespace BlogEngine.Shared.Contracts;

/// <summary>
/// What the admin dashboard shows (design 4.5 O1, 12.1, T3.10, T4.26): post counts, the moderation queue size, scheduled
/// posts, recent activity, and whether to nudge the admin to secure their account.
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

    /// <summary>The next scheduled posts to go live, soonest first (design 6.3, A9).</summary>
    public List<PostSummaryDto> ScheduledPosts { get; set; } = [];

    /// <summary>The most recently changed posts, newest first.</summary>
    public List<PostSummaryDto> RecentPosts { get; set; } = [];

    /// <summary>The most recent comments of any status, newest first.</summary>
    public List<CommentDto> RecentComments { get; set; } = [];

    /// <summary>
    /// Whether the signed-in admin has neither a passkey nor two-factor authentication, so the dashboard suggests setting one
    /// up (design 12.1).
    /// </summary>
    public bool ShowSecurityNudge { get; set; }
}