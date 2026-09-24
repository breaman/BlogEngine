using BlogEngine.Shared.Markdown;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Turns a comment's Markdown into the HTML stored in <c>Comment.BodyHtml</c> (design 8.2, T3.2): the restricted
/// comment pipeline of <see cref="BlogMarkdownPipeline"/>, then <see cref="CommentHtmlSanitizer"/>.
/// </summary>
/// <remarks>Rendering happens once, on submit, so the post page only ever reads stored, sanitized HTML.</remarks>
public sealed class CommentRenderer(CommentHtmlSanitizer sanitizer)
{
    /// <summary>Renders and sanitizes <paramref name="markdown"/>.</summary>
    public string Render(string? markdown)
    {
        return sanitizer.Sanitize(BlogMarkdownPipeline.Default.RenderComment(markdown).Html);
    }
}
