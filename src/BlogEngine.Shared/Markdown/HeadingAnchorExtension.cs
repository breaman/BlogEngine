using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Markdig extension that makes every heading with an id a link to itself (P13), so readers can copy a link to a
/// section: <c>&lt;h2 id="setup"&gt;&lt;a class="heading-anchor" href="#setup"&gt;Setup&lt;/a&gt;&lt;/h2&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The heading text itself is the link (the pattern MDN uses) rather than an extra "#" or icon link: the heading's
/// accessible name stays its text, keyboard users can reach the link, and there is no extra markup to skip in the
/// table of contents. The "#" readers see on hover comes from CSS (<c>site.scss</c>).
/// </para>
/// <para>
/// Headings that already contain a link are left alone, because links can't be nested. Ids come from the
/// auto-identifiers extension (or an explicit <c>{#id}</c>), which assigns them while inlines are parsed, before
/// <see cref="MarkdownPipelineBuilder.DocumentProcessed"/> runs this rewrite.
/// </para>
/// </remarks>
public sealed class HeadingAnchorExtension : IMarkdownExtension
{
    /// <summary>Class of the link wrapped around a heading's text.</summary>
    public const string AnchorClass = "heading-anchor";

    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= Rewrite;
        pipeline.DocumentProcessed += Rewrite;
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    /// <summary>Wraps the inline content of each eligible heading in a link to the heading's id.</summary>
    private static void Rewrite(MarkdownDocument document)
    {
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var id = heading.TryGetAttributes()?.Id;
            if (string.IsNullOrEmpty(id) || heading.Inline?.FirstChild is null || ContainsLink(heading.Inline))
            {
                continue;
            }

            var link = new LinkInline($"#{id}", string.Empty);
            link.GetAttributes().AddClass(AnchorClass);

            // Materialize first: moving a child changes the sibling chain being enumerated.
            foreach (var child in heading.Inline.ToList())
            {
                child.Remove();
                link.AppendChild(child);
            }

            heading.Inline.AppendChild(link);
        }
    }

    /// <summary>Whether the heading has a Markdown link, autolink or raw <c>&lt;a&gt;</c> tag of its own.</summary>
    private static bool ContainsLink(ContainerInline inline)
    {
        return inline.Descendants<LinkInline>().Any(l => !l.IsImage)
            || inline.Descendants<AutolinkInline>().Any()
            || inline.Descendants<HtmlInline>().Any(h => IsAnchorTag(h.Tag));
    }

    /// <summary>Whether raw inline HTML opens an <c>&lt;a&gt;</c> element (and not, say, <c>&lt;abbr&gt;</c>).</summary>
    private static bool IsAnchorTag(string? tag)
    {
        return tag is { Length: >= 3 }
            && tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase)
            && (tag[2] == '>' || char.IsWhiteSpace(tag[2]));
    }
}
