namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Output of rendering Markdown with <see cref="BlogMarkdownPipeline"/>.
/// </summary>
/// <param name="Html">The rendered HTML. Not sanitized; the server sanitizes before storing or serving it.</param>
/// <param name="ContainsCodeBlocks">
/// Whether the content has fenced or indented code blocks, so the public post page loads the
/// highlighting and copy-button script only when needed (design 10.3).
/// </param>
public sealed record MarkdownRenderResult(string Html, bool ContainsCodeBlocks);