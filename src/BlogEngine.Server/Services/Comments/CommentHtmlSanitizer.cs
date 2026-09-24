using AngleSharp.Dom;

using BlogEngine.Shared.Markdown;

using Ganss.Xss;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Sanitizes rendered comment HTML with a strict allowlist (design 8.2, 12.2) and forces
/// <c>rel="nofollow ugc noopener"</c> on every link.
/// </summary>
/// <remarks>
/// <para>
/// The comment pipeline already refuses raw HTML, headings, images and tables, but Markdig is not a sanitizer, so the
/// output always goes through here as well. Only the elements that pipeline produces are kept: paragraphs, emphasis,
/// code, links, quotes and lists, plus the external-link icon <see cref="ExternalLinkRewriter"/> adds.
/// </para>
/// <para>
/// <c>nofollow ugc</c> tells search engines the links are user-generated and passes no ranking to them, which removes
/// the payoff of link spam.
/// </para>
/// <para>
/// Uses the HtmlSanitizer (Ganss.Xss) package; instances are thread-safe once configured, so this is a singleton.
/// </para>
/// </remarks>
public sealed class CommentHtmlSanitizer
{
    /// <summary>The <c>rel</c> value on every link in a comment, and on the commenter's website link.</summary>
    public const string LinkRel = "nofollow ugc noopener";

    /// <summary>Classes that survive: the external-link marker and its Bootstrap icon.</summary>
    private static readonly HashSet<string> AllowedClasses = new(StringComparer.Ordinal)
    {
        ExternalLinkRewriter.LinkClass,
        "external-link-icon",
        "bi",
        "bi-box-arrow-up-right"
    };

    private readonly HtmlSanitizer sanitizer;

    /// <summary>Configures the comment allowlist.</summary>
    public CommentHtmlSanitizer()
    {
        sanitizer = new HtmlSanitizer();

        // Replace HtmlSanitizer's broad defaults with exactly what the comment pipeline can produce.
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "br", "em", "strong", "del", "code", "pre", "blockquote", "ul", "ol", "li", "a", "i"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "rel", "class", "aria-hidden", "start"]);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();

        // A non-empty set means only these classes survive (language-* classes on code blocks are dropped).
        sanitizer.AllowedClasses.UnionWith(AllowedClasses);

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement { LocalName: "a" } link)
            {
                link.SetAttribute("rel", LinkRel);
            }
        };
    }

    /// <summary>Returns <paramref name="html"/> with anything outside the comment allowlist removed.</summary>
    public string Sanitize(string? html)
    {
        return string.IsNullOrEmpty(html) ? string.Empty : sanitizer.Sanitize(html);
    }
}
