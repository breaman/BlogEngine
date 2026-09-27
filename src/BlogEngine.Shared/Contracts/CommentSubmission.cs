namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A reader's comment as submitted on the public post page (design 8.1, C1, Q1). Commenters have no accounts, so
/// the name and email are all that identify them.
/// </summary>
public sealed class CommentSubmission
{
    /// <summary>Display name shown with the comment.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Email address; never displayed, used for blocking and optional avatars.</summary>
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>Optional website, linked from the author name with <c>rel="nofollow ugc noopener"</c>.</summary>
    public string? AuthorUrl { get; set; }

    /// <summary>The comment in the restricted comment Markdown dialect (design 8.2).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// The approved comment this replies to, from the post page's "Reply" link (C6); <see langword="null"/> for a new
    /// thread. Threads are one level deep, so a reply to a reply is stored under the top-level comment, and a parent
    /// that isn't shown on the post (any more) makes this a new thread instead.
    /// </summary>
    public int? ParentCommentId { get; set; }
}