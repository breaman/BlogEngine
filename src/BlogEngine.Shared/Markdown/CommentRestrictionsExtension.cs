using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Removes constructs from parsed comments that the comment pipeline doesn't allow but that can't be
/// switched off by removing a parser (design 8.2): images become plain links, and thematic breaks
/// (horizontal rules) are dropped.
/// </summary>
/// <remarks>
/// <para>
/// Markdig parses links and images with the same parser, and its list parser detects thematic breaks
/// through a shared parser instance, so neither can be disabled at parse time. Rewriting the document
/// after parsing covers both.
/// </para>
/// <para>
/// Images are turned into links rather than removed, keeping the URL reachable for readers without
/// embedding anything. The server sanitizer still strips any <c>img</c> tag as a second line of defense.
/// </para>
/// </remarks>
internal sealed class CommentRestrictionsExtension : IMarkdownExtension
{
    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= Restrict;
        pipeline.DocumentProcessed += Restrict;
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    /// <summary>Applies every restriction to the parsed document.</summary>
    private static void Restrict(MarkdownDocument document)
    {
        RemoveThematicBreaks(document);
        ConvertImagesToLinks(document);
    }

    /// <summary>Removes horizontal rules wherever they appear, including inside lists and quotes.</summary>
    private static void RemoveThematicBreaks(MarkdownDocument document)
    {
        var breaks = document.Descendants<ThematicBreakBlock>().ToList();

        foreach (var thematicBreak in breaks)
        {
            thematicBreak.Parent?.Remove(thematicBreak);
        }
    }

    /// <summary>Rewrites every image as a link whose text is the alt text, or the URL when there is none.</summary>
    private static void ConvertImagesToLinks(MarkdownDocument document)
    {
        var images = document.Descendants<LinkInline>().Where(link => link.IsImage).ToList();

        foreach (var image in images)
        {
            image.IsImage = false;
            if (image.FirstChild is null)
            {
                image.AppendChild(new LiteralInline(image.Url ?? string.Empty));
            }
        }
    }
}