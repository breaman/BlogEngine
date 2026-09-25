namespace BlogEngine.Shared.Contracts;

/// <summary>
/// The author's reply to a comment (<c>POST /api/admin/comments/{id}/reply</c>, design 8.4, C5). The reply is
/// published straight away, marked as the author's, and approves the comment it answers.
/// </summary>
public sealed class CommentReplyRequest
{
    /// <summary>The reply in the restricted comment Markdown dialect (design 8.2).</summary>
    public string Body { get; set; } = string.Empty;
}
