using System.Text;

using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Extracts the text a reader actually reads from a parsed Markdown document: no Markdown syntax, no
/// link URLs, no raw HTML, no images and no code blocks.
/// </summary>
internal static class MarkdownPlainText
{
    /// <summary>
    /// Everything a reader reads: paragraphs, headings, list items, quotes, table cells, captions and
    /// footnotes, one block per line. Used for word counts.
    /// </summary>
    public static string ReadableText(MarkdownDocument document)
    {
        var builder = new StringBuilder();
        AppendBlock(document, builder, paragraphsOnly: false);
        return builder.ToString();
    }

    /// <summary>
    /// Prose paragraphs only (including those inside lists and quotes), one per line. Headings, tables and
    /// footnotes are left out because they read badly as a summary.
    /// </summary>
    public static string ProseText(MarkdownDocument document)
    {
        var builder = new StringBuilder();
        AppendBlock(document, builder, paragraphsOnly: true);
        return builder.ToString();
    }

    /// <summary>Appends the text of a block and its descendants.</summary>
    private static void AppendBlock(Block block, StringBuilder builder, bool paragraphsOnly)
    {
        switch (block)
        {
            // Code and raw HTML are not prose. FencedCodeBlock derives from CodeBlock.
            case CodeBlock or HtmlBlock:
                return;

            case Table or FootnoteGroup when paragraphsOnly:
                return;

            case LeafBlock { Inline: { } inline } leaf:
                if (paragraphsOnly && leaf is not ParagraphBlock)
                {
                    return;
                }

                AppendInline(inline, builder);
                builder.Append('\n');
                return;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    AppendBlock(child, builder, paragraphsOnly);
                }

                return;
        }
    }

    /// <summary>Appends the visible text of an inline and its children.</summary>
    private static void AppendInline(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.ToString());
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;

            // Images (including media embeds), raw HTML and footnote markers aren't read as words.
            case LinkInline { IsImage: true }:
            case HtmlInline:
            case FootnoteLink:
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    AppendInline(child, builder);
                }

                break;
        }
    }
}