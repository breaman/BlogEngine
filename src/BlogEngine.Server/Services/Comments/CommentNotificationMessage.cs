using System.Net;
using System.Text;

using BlogEngine.Server.Services.Email;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Composes the "new comment awaiting moderation" email (design 8.4, C9): who commented on which post, the comment
/// itself, the spam guard's score, and links to the post and the moderation queue.
/// </summary>
/// <remarks>
/// Everything a commenter typed is HTML-encoded, except the body, which is the HTML sanitized when the comment was
/// stored. The commenter's email is left out; the moderation queue shows it.
/// </remarks>
public static class CommentNotificationMessage
{
    /// <summary>Path of the moderation queue, relative to the site root.</summary>
    public const string ModerationPath = "admin/comments";

    /// <summary>Builds the email for <paramref name="comment"/>.</summary>
    /// <param name="comment">The pending comment.</param>
    /// <param name="recipients">Who gets the email.</param>
    /// <param name="siteRoot">The site's absolute root URL for links, or <see langword="null"/> to leave them out.</param>
    public static EmailMessage Create(CommentNotificationContent comment, IReadOnlyList<string> recipients, Uri? siteRoot)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(recipients);

        var postUrl = siteRoot is not null && comment.PostPath is { } path
            ? new Uri(siteRoot, $"{path.TrimStart('/')}#comment-{comment.CommentId}").AbsoluteUri
            : null;
        var moderationUrl = siteRoot is null ? null : new Uri(siteRoot, ModerationPath).AbsoluteUri;
        var name = WebUtility.HtmlEncode(comment.AuthorName);
        var title = WebUtility.HtmlEncode(comment.PostTitle);

        var html = new StringBuilder();
        html.Append("<p><strong>").Append(name).Append("</strong> commented on ");
        html.Append(postUrl is null ? $"<em>{title}</em>" : $"<a href=\"{WebUtility.HtmlEncode(postUrl)}\">{title}</a>");
        html.Append(":</p>");
        html.Append("<blockquote style=\"margin:0 0 1em;padding-left:1em;border-left:3px solid #ccc\">").Append(comment.BodyHtml).Append("</blockquote>");

        var text = new StringBuilder();
        text.Append(comment.AuthorName).Append(" commented on \"").Append(comment.PostTitle).AppendLine("\":");
        text.AppendLine().AppendLine(comment.BodyMarkdown).AppendLine();

        if (comment.SpamScore > 0)
        {
            var reasons = string.IsNullOrWhiteSpace(comment.SpamReasons) ? string.Empty : $" ({comment.SpamReasons})";
            html.Append("<p>Spam score: ").Append(comment.SpamScore).Append(WebUtility.HtmlEncode(reasons)).Append("</p>");
            text.Append("Spam score: ").Append(comment.SpamScore).AppendLine(reasons);
        }

        if (moderationUrl is not null)
        {
            html.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(moderationUrl)).Append("\">Approve or reject it in the moderation queue</a></p>");
            text.Append("Moderate it at ").AppendLine(moderationUrl);
        }

        if (postUrl is not null)
        {
            text.Append("Post: ").AppendLine(postUrl);
        }

        return new EmailMessage(recipients, $"New comment on \"{comment.PostTitle}\" awaiting moderation", html.ToString(), text.ToString());
    }
}