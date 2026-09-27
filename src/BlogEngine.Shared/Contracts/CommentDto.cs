using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A comment as the moderation queue shows it (design 8.4): unlike the public list it includes the commenter's
/// email and the spam guard's verdict.
/// </summary>
public sealed class CommentDto
{
    /// <summary>The comment id.</summary>
    public int Id { get; set; }

    /// <summary>The post commented on.</summary>
    public int PostId { get; set; }

    /// <summary>Title of the post.</summary>
    public string PostTitle { get; set; } = string.Empty;

    /// <summary>Public path of the post while it is published, otherwise <see langword="null"/>.</summary>
    public string? PostPath { get; set; }

    /// <summary>The comment this replies to, if any.</summary>
    public int? ParentCommentId { get; set; }

    /// <summary>Commenter display name.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Commenter email; shown to the moderator only.</summary>
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>Commenter website, if given.</summary>
    public string? AuthorUrl { get; set; }

    /// <summary>The sanitized HTML of the comment.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Moderation state.</summary>
    public CommentStatus Status { get; set; }

    /// <summary>Whether the blog author wrote it.</summary>
    public bool IsAuthorReply { get; set; }

    /// <summary>Score from the spam guard; 50 or more is spam.</summary>
    public int SpamScore { get; set; }

    /// <summary>Why the spam guard scored it, if it did.</summary>
    public string? SpamReasons { get; set; }

    /// <summary>When it was submitted.</summary>
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When it was last moderated.</summary>
    public DateTimeOffset? ModeratedOn { get; set; }
}