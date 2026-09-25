using System.Globalization;
using System.Net;
using System.Text;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

using Markdig.Helpers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Turns Markdown images that point at the media library into sized, lazily loaded images (design 9.4, 9.5, T2.7).
/// </summary>
/// <remarks>
/// <para>
/// An image alone in its paragraph (the way the media picker inserts it) becomes a figure, with the Markdown
/// title as the caption and any <c>{.class}</c> hints (<c>.img-medium</c>, <c>.img-small</c>,
/// <c>.img-center</c>) on the figure:
/// </para>
/// <code>
/// ![Sunset over the lake](/media/ab12cd34ef56/sunset.jpg "At dusk"){.img-medium}
/// </code>
/// <code>
/// &lt;figure class="media-figure img-medium"&gt;&lt;img src="/media/ab12cd34ef56/sunset.jpg?v=3" alt="Sunset over the lake"
///   width="1280" height="853" loading="lazy" decoding="async"&gt;&lt;figcaption&gt;At dusk&lt;/figcaption&gt;&lt;/figure&gt;
/// </code>
/// <para>
/// An image inside other text (or inside a link) stays inline and only gains its size, version and lazy loading.
/// Explicit <c>width</c>/<c>height</c> prevent layout shift, and <c>?v=</c> lets browsers cache the file forever.
/// </para>
/// <para>
/// When the item has responsive renditions (M6, T4.19), the image is wrapped in a <c>&lt;picture&gt;</c>: a WebP
/// <c>&lt;source&gt;</c> and the <c>&lt;img&gt;</c> both list the renditions in <c>srcset</c>, with <c>sizes</c> matching
/// the size hint, so the browser downloads the smallest copy that looks sharp. The <c>&lt;img&gt;</c> falls back to the
/// rendition closest to <see cref="FallbackWidth"/> in the image's own format.
/// </para>
/// <para>
/// When the item no longer exists (deleted with "Delete anyway", design 9.6), the admin preview shows a visible
/// placeholder and published HTML silently leaves the image out. Images outside the library are not touched.
/// </para>
/// <para>
/// This rewrites the parsed document rather than hooking a renderer, so the server and the WebAssembly preview
/// produce the same HTML. It needs the library's metadata, which the caller supplies as an <see cref="IMediaLookup"/>.
/// </para>
/// </remarks>
public sealed class MediaLinkRewriter
{
    /// <summary>Class on every figure the rewriter produces.</summary>
    public const string FigureClass = "media-figure";

    /// <summary>Class on the placeholder shown in the preview for a deleted item.</summary>
    public const string MissingClass = "media-missing";

    /// <summary>Width of the <c>src</c> fallback when an image has renditions: wide enough for the post column.</summary>
    public const int FallbackWidth = 1280;

    private readonly HashSet<string> internalHosts;

    /// <summary>Creates the rewriter.</summary>
    /// <param name="internalHosts">
    /// Host names that belong to the blog, so absolute URLs such as <c>https://blog.example.com/media/…</c> are
    /// recognized as library images too.
    /// </param>
    public MediaLinkRewriter(IEnumerable<string>? internalHosts = null)
    {
        this.internalHosts = new HashSet<string>(internalHosts ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rewrites every library image in <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The parsed post.</param>
    /// <param name="lookup">Metadata of the library items the post uses.</param>
    /// <param name="preview">
    /// <see langword="true"/> for the editor preview: missing items get a visible placeholder and figures carry the
    /// <c>data-line</c> used for scroll sync. <see langword="false"/> for published HTML, which omits missing items.
    /// </param>
    public void Rewrite(MarkdownDocument document, IMediaLookup lookup, bool preview)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lookup);

        // Materialize first: the rewrite replaces nodes in the tree being enumerated.
        var images = document.Descendants<LinkInline>()
            .Where(link => link.IsImage)
            .Select(link => (Link: link, Target: TryParseLibraryUrl(link.GetDynamicUrl?.Invoke() ?? link.Url)))
            .Where(image => image.Target is not null)
            .ToList();

        foreach (var (image, target) in images)
        {
            var item = lookup.Find(target!.Value.PublicId);
            if (StandaloneParagraph(image) is { } paragraph)
            {
                ReplaceParagraph(paragraph, FigureHtml(image, item, target.Value.FileName, preview, paragraph.Line));
            }
            else
            {
                // The alt text is already in the HTML, so the link's children must not be carried over.
                image.ReplaceBy(new HtmlInline(InlineHtml(image, item, target.Value.FileName, preview)), copyChildren: false);
            }
        }
    }

    /// <summary>
    /// The public id and file name when <paramref name="url"/> is a library URL: <c>/media/{publicId}/{fileName}</c>,
    /// relative or on one of the blog's own hosts, optionally with a query string.
    /// </summary>
    public (string PublicId, string FileName)? TryParseLibraryUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = url;
        if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal))
        {
            var candidate = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !internalHosts.Contains(uri.Host))
            {
                return null;
            }

            path = uri.AbsolutePath;
        }

        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0)
        {
            path = path[..end];
        }

        var segments = path.Split('/');
        return segments is ["", "media", { Length: FieldLengths.MediaPublicId } publicId, { Length: > 0 } fileName]
            && publicId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? (publicId, Uri.UnescapeDataString(fileName))
            : null;
    }

    /// <summary>The image's paragraph when the image is the only thing in it (whitespace aside), otherwise null.</summary>
    private static ParagraphBlock? StandaloneParagraph(LinkInline image)
    {
        if (image.Parent is not { } container || container.Parent is not null
            || container.ParentBlock is not ParagraphBlock paragraph)
        {
            return null;
        }

        foreach (var inline in container)
        {
            var isBlank = inline == image
                || inline is LineBreakInline
                || (inline is LiteralInline literal && literal.Content.IsEmptyOrWhitespace());
            if (!isBlank)
            {
                return null;
            }
        }

        return paragraph;
    }

    /// <summary>Swaps a paragraph for raw HTML, or removes it when there is nothing to show.</summary>
    private static void ReplaceParagraph(ParagraphBlock paragraph, string html)
    {
        var parent = paragraph.Parent!;
        var index = parent.IndexOf(paragraph);
        parent.RemoveAt(index);
        if (html.Length == 0)
        {
            return;
        }

        var block = new HtmlBlock(null)
        {
            Type = HtmlBlockType.NonInterruptingBlock,
            Line = paragraph.Line,
            Column = paragraph.Column,
            Span = paragraph.Span,
            Lines = new StringLineGroup(html)
        };
        parent.Insert(index, block);
    }

    /// <summary>A figure with a caption for a standalone image, a placeholder in the preview, or nothing.</summary>
    private static string FigureHtml(LinkInline image, MediaLookupItem? item, string fileName, bool preview, int sourceLine)
    {
        if (item is null && !preview)
        {
            return string.Empty;
        }

        var attributes = image.TryGetAttributes();
        var classes = new List<string> { FigureClass };
        if (item is null)
        {
            classes.Add(MissingClass);
        }

        classes.AddRange(attributes?.Classes ?? []);

        var html = new StringBuilder("<figure");
        AppendAttribute(html, "id", attributes?.Id);
        AppendAttribute(html, "class", string.Join(' ', classes.Distinct(StringComparer.Ordinal)));
        if (preview)
        {
            AppendAttribute(html, BlogMarkdownPipeline.SourceLineAttribute, sourceLine.ToString(CultureInfo.InvariantCulture));
        }

        html.Append('>');
        html.Append(item is null ? MissingPlaceholder(fileName, "div") : ImageTag(image, item, cssClass: null, classes));

        if (item is not null && !string.IsNullOrWhiteSpace(image.Title))
        {
            html.Append("<figcaption>").Append(WebUtility.HtmlEncode(image.Title.Trim())).Append("</figcaption>");
        }

        return html.Append("</figure>").ToString();
    }

    /// <summary>An inline image with its size and version, a placeholder in the preview, or nothing.</summary>
    private static string InlineHtml(LinkInline image, MediaLookupItem? item, string fileName, bool preview)
    {
        if (item is null)
        {
            return preview ? MissingPlaceholder(fileName, "span") : string.Empty;
        }

        var classes = image.TryGetAttributes()?.Classes;
        return ImageTag(image, item, classes is { Count: > 0 } ? string.Join(' ', classes) : null, classes ?? []);
    }

    /// <summary>
    /// The <c>&lt;img&gt;</c> tag for the current version of an item, inside a <c>&lt;picture&gt;</c> with its renditions
    /// when it has any.
    /// </summary>
    /// <param name="image">The Markdown image.</param>
    /// <param name="item">The library item.</param>
    /// <param name="cssClass">Classes for the <c>&lt;img&gt;</c> (inline images carry their size hints themselves).</param>
    /// <param name="hints">The size hints that decide <c>sizes</c>: the figure's or the image's classes.</param>
    private static string ImageTag(LinkInline image, MediaLookupItem item, string? cssClass, IEnumerable<string> hints)
    {
        var alt = InlineText(image).Trim();
        if (alt.Length == 0)
        {
            alt = item.AltText;
        }

        var widths = item.Renditions;
        var html = new StringBuilder();
        string? sizes = null;
        if (widths.Count > 0)
        {
            sizes = SizesFor(hints);
            html.Append("<picture><source type=\"image/webp\"");
            AppendAttribute(html, "srcset", SrcSet(item, webp: true));
            AppendAttribute(html, "sizes", sizes);
            html.Append('>');
        }

        html.Append("<img");
        AppendAttribute(html, "src", widths.Count > 0
            ? MediaPaths.Rendition(item.PublicId, item.FileName, item.Version, FallbackRendition(widths))
            : MediaPaths.Versioned(item.PublicId, item.FileName, item.Version));
        if (widths.Count > 0)
        {
            AppendAttribute(html, "srcset", SrcSet(item, webp: false));
            AppendAttribute(html, "sizes", sizes);
        }

        AppendAttribute(html, "alt", alt, always: true);
        AppendAttribute(html, "width", item.Width.ToString(CultureInfo.InvariantCulture));
        AppendAttribute(html, "height", item.Height.ToString(CultureInfo.InvariantCulture));
        AppendAttribute(html, "class", cssClass);
        AppendAttribute(html, "loading", "lazy");
        AppendAttribute(html, "decoding", "async");
        html.Append('>');

        return (widths.Count > 0 ? html.Append("</picture>") : html).ToString();
    }

    /// <summary>The <c>srcset</c> of an item's renditions, in WebP or in its own format.</summary>
    private static string SrcSet(MediaLookupItem item, bool webp)
    {
        return string.Join(", ", item.Renditions.Select(width => string.Create(CultureInfo.InvariantCulture,
            $"{MediaPaths.Rendition(item.PublicId, item.FileName, item.Version, width, webp)} {width}w")));
    }

    /// <summary>The rendition used as <c>src</c>: the widest up to <see cref="FallbackWidth"/>, or the narrowest one.</summary>
    private static int FallbackRendition(IReadOnlyList<int> widths)
    {
        var fitting = widths.Where(w => w <= FallbackWidth).ToList();
        return fitting.Count > 0 ? fitting.Max() : widths.Min();
    }

    /// <summary>
    /// How wide the image is shown, for <c>sizes</c>: the post column is at most 48rem, medium images take two thirds of
    /// it and small ones a third, and on phones (below 576px) medium images go full width and small ones half
    /// (matching <c>site.scss</c>).
    /// </summary>
    public static string SizesFor(IEnumerable<string> hints)
    {
        var classes = hints.ToHashSet(StringComparer.Ordinal);
        return classes.Contains("img-small") ? "(min-width: 48rem) 16rem, (min-width: 576px) 34vw, 50vw"
            : classes.Contains("img-medium") ? "(min-width: 48rem) 32rem, (min-width: 576px) 67vw, 100vw"
            : "(min-width: 48rem) 48rem, 100vw";
    }

    /// <summary>What the admin preview shows for an item that was deleted.</summary>
    private static string MissingPlaceholder(string fileName, string element)
    {
        var name = WebUtility.HtmlEncode(fileName);
        return $"""<{element} class="media-missing-placeholder" role="img" aria-label="Missing image {name}"><i class="bi bi-image-alt" aria-hidden="true"></i> Missing image: {name}</{element}>""";
    }

    /// <summary>The plain text of an image's alt text, the way Markdig renders it into the <c>alt</c> attribute.</summary>
    private static string InlineText(ContainerInline container)
    {
        var text = new StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded.AsSpan());
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                case ContainerInline nested:
                    text.Append(InlineText(nested));
                    break;
            }
        }

        return text.ToString();
    }

    private static void AppendAttribute(StringBuilder html, string name, string? value, bool always = false)
    {
        if (always || !string.IsNullOrEmpty(value))
        {
            html.Append(' ').Append(name).Append("=\"").Append(WebUtility.HtmlEncode(value ?? string.Empty)).Append('"');
        }
    }
}
