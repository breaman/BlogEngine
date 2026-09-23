using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Models;

/// <summary>
/// A reader comment or author reply on a post (design 6.5). Only <see cref="CommentStatus.Approved"/>
/// comments are shown publicly, and the author's email is never displayed.
/// </summary>
public class Comment : FingerPrintEntityBase
{
    /// <summary>The post commented on.</summary>
    public int PostId { get; set; }

    /// <summary>The post.</summary>
    public Post Post { get; set; } = null!;

    /// <summary>
    /// The top-level comment this replies to. Threads are one level deep: a reply to a reply attaches
    /// to the top-level parent.
    /// </summary>
    public int? ParentCommentId { get; set; }

    /// <summary>The parent comment.</summary>
    public Comment? ParentComment { get; set; }

    /// <summary>Replies to this comment.</summary>
    public List<Comment> Replies { get; set; } = [];

    /// <summary>Commenter display name.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Commenter email; used for notifications, blocking and avatar hashes, never displayed.</summary>
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>Optional commenter website, rendered with <c>rel="nofollow ugc noopener"</c>.</summary>
    public string? AuthorUrl { get; set; }

    /// <summary>Comment body in the restricted comment Markdown dialect.</summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>Sanitized HTML rendered from <see cref="BodyMarkdown"/>.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Moderation state.</summary>
    public CommentStatus Status { get; set; }

    /// <summary>Whether the blog author wrote this reply.</summary>
    public bool IsAuthorReply { get; set; }

    /// <summary>Lowercase hex SHA-256 of the commenter's IP plus a secret salt; the raw IP is never stored.</summary>
    public string IpHash { get; set; } = string.Empty;

    /// <summary>Commenter user agent, kept as moderation context.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Score assigned by the spam guard; 50 or more is spam.</summary>
    public int SpamScore { get; set; }

    /// <summary>Human-readable reasons behind <see cref="SpamScore"/>, shown to the moderator.</summary>
    public string? SpamReasons { get; set; }

    /// <summary>When the comment was last moderated.</summary>
    public DateTimeOffset? ModeratedOn { get; set; }

    /// <summary>User ID of the moderator.</summary>
    public int? ModeratedBy { get; set; }
}
