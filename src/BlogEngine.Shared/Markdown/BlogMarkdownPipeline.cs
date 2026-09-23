using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Parsers;
using Markdig.Syntax;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// The blog's Markdown pipelines: a full-featured one for posts and pages, and a restricted one for
/// reader comments (design 10.1, 8.2).
/// </summary>
/// <remarks>
/// <para>
/// The same code renders posts on the server at save time and the live preview in the WebAssembly editor
/// (Markdig is pure managed code), so the preview is exactly what gets published. Nothing here may use
/// server-only APIs.
/// </para>
/// <para>
/// Markdig pipelines are immutable and thread-safe once built, so one instance can be shared (register it
/// as a singleton, or use <see cref="Default"/>).
/// </para>
/// <para>
/// The output is <b>not</b> sanitized. Markdig is not a sanitizer; the server runs the HTML through
/// <c>HtmlSanitizer</c> before storing or serving it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = BlogMarkdownPipeline.Default.RenderPost(post.ContentMarkdown);
/// post.ContentHtml = sanitizer.Sanitize(result.Html);
/// var needsHighlighting = result.ContainsCodeBlocks;
/// </code>
/// </example>
public sealed class BlogMarkdownPipeline
{
    /// <summary>A pipeline with no internal hosts configured; every absolute http(s) link counts as external.</summary>
    public static BlogMarkdownPipeline Default { get; } = new();

    /// <summary>Builds both pipelines.</summary>
    /// <param name="options">Host-specific settings; defaults are used when <see langword="null"/>.</param>
    public BlogMarkdownPipeline(BlogMarkdownOptions? options = null)
    {
        options ??= new BlogMarkdownOptions();
        PostPipeline = BuildPostPipeline(options);
        CommentPipeline = BuildCommentPipeline(options);
    }

    /// <summary>The post and page pipeline. Raw HTML is allowed because the author is trusted.</summary>
    public MarkdownPipeline PostPipeline { get; }

    /// <summary>
    /// The comment pipeline: paragraphs, emphasis, inline code, code blocks, links, blockquotes and lists
    /// only. Headings, images, tables and raw HTML are not rendered.
    /// </summary>
    public MarkdownPipeline CommentPipeline { get; }

    /// <summary>
    /// Parses post Markdown without rendering it, for callers that walk the syntax tree (reading time,
    /// summaries, editor scroll sync).
    /// </summary>
    public MarkdownDocument ParsePost(string? markdown)
    {
        return Markdig.Markdown.Parse(markdown ?? string.Empty, PostPipeline);
    }

    /// <summary>Renders post or page Markdown to HTML.</summary>
    public MarkdownRenderResult RenderPost(string? markdown)
    {
        return Render(markdown, PostPipeline);
    }

    /// <summary>Renders comment Markdown to HTML with the restricted comment pipeline.</summary>
    public MarkdownRenderResult RenderComment(string? markdown)
    {
        return Render(markdown, CommentPipeline);
    }

    /// <summary>Parses once, then renders the document and inspects it for code blocks.</summary>
    private static MarkdownRenderResult Render(string? markdown, MarkdownPipeline pipeline)
    {
        var document = Markdig.Markdown.Parse(markdown ?? string.Empty, pipeline);
        var html = document.ToHtml(pipeline);

        // FencedCodeBlock derives from CodeBlock, so this covers fenced and indented blocks.
        var containsCodeBlocks = document.Descendants<CodeBlock>().Any();

        return new MarkdownRenderResult(html, containsCodeBlocks);
    }

    /// <summary>Bare <c>www.</c> links default to https rather than Markdig's http.</summary>
    private static AutoLinkOptions CreateAutoLinkOptions()
    {
        return new AutoLinkOptions { UseHttpsForWWWLinks = true };
    }

    /// <summary>Builds the full-featured post pipeline listed in design 10.1.</summary>
    private static MarkdownPipeline BuildPostPipeline(BlogMarkdownOptions options)
    {
        return new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseFootnotes()
            // GitHub-style heading ids, so anchors match what authors expect from READMEs.
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            // Only strikethrough (~~) and marked (==). Subscript would turn a single "~" into markup.
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
            .UseAutoLinks(CreateAutoLinkOptions())
            .UseMediaLinks(PrivacyEnhancedMediaHosts.CreateOptions())
            .UseFigures()
            // Exact source spans on every node, for scroll sync between the editor and preview.
            .UsePreciseSourceLocation()
            .Use(new ExternalLinkRewriter(options.InternalHosts))
            // Must be registered last so the {.class} syntax applies to everything parsed above.
            .UseGenericAttributes()
            .Build();
    }

    /// <summary>Builds the restricted comment pipeline described in design 8.2.</summary>
    private static MarkdownPipeline BuildCommentPipeline(BlogMarkdownOptions options)
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAutoLinks(CreateAutoLinkOptions())
            // Raw HTML is rendered as escaped text rather than markup.
            .DisableHtml()
            // Drops images and horizontal rules. Registered before the link rewriter so links made from
            // images are decorated too.
            .Use<CommentRestrictionsExtension>()
            .Use(new ExternalLinkRewriter(options.InternalHosts));

        // Without the heading parser, "# Heading" stays as ordinary paragraph text.
        builder.BlockParsers.RemoveAll(parser => parser is HeadingBlockParser);

        // Setext headings ("Title" underlined with === or ---) are parsed by the paragraph parser.
        var paragraphParser = builder.BlockParsers.Find<ParagraphBlockParser>()
            ?? throw new InvalidOperationException("Markdig's default pipeline has no paragraph parser.");
        paragraphParser.ParseSetexHeadings = false;

        return builder.Build();
    }
}