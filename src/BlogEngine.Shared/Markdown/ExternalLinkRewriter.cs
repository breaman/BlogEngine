using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Markdig extension that marks links to other sites: adds <c>rel="noopener"</c>, an
/// <c>external-link</c> class, and a small Bootstrap icon after the link text (design 10.1).
/// </summary>
/// <remarks>
/// Works on the parsed document rather than in a renderer, so it composes with every other extension
/// and runs identically on the server and in the WebAssembly preview. Values already set on a link
/// (for example through generic attributes) are kept.
/// </remarks>
public sealed class ExternalLinkRewriter : IMarkdownExtension
{
    /// <summary>Class added to every external link.</summary>
    public const string LinkClass = "external-link";

    /// <summary>Decorative icon appended inside the link; hidden from screen readers.</summary>
    public const string IconHtml = """<i class="bi bi-box-arrow-up-right external-link-icon" aria-hidden="true"></i>""";

    private readonly HashSet<string> internalHosts;

    /// <summary>Creates the rewriter.</summary>
    /// <param name="internalHosts">Host names that belong to the blog itself; links to them are internal.</param>
    public ExternalLinkRewriter(IEnumerable<string>? internalHosts = null)
    {
        this.internalHosts = new HashSet<string>(internalHosts ?? [], StringComparer.OrdinalIgnoreCase);
    }

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

    /// <summary>
    /// Whether <paramref name="url"/> points to another site: an absolute or protocol-relative http(s) URL
    /// whose host is not one of the blog's own.
    /// </summary>
    public bool IsExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Protocol-relative URLs ("//example.com") otherwise parse as file paths on some platforms.
        var candidate = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !internalHosts.Contains(uri.Host);
    }

    /// <summary>Decorates every external, non-image link in the parsed document.</summary>
    private void Rewrite(MarkdownDocument document)
    {
        // Materialize first: appending the icon adds inlines to the tree being enumerated.
        var externalLinks = document.Descendants<LinkInline>()
            .Where(link => !link.IsImage && IsExternal(link.GetDynamicUrl?.Invoke() ?? link.Url))
            .ToList();

        foreach (var link in externalLinks)
        {
            var attributes = link.GetAttributes();
            attributes.AddPropertyIfNotExist("rel", "noopener");
            attributes.AddClass(LinkClass);
            link.AppendChild(new HtmlInline(IconHtml));
        }
    }
}