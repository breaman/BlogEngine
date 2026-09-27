using Ganss.Xss;

namespace BlogEngine.Server.Services;

/// <summary>
/// Sanitizes rendered post HTML with a permissive allowlist (design 10.1, 12.2).
/// </summary>
/// <remarks>
/// <para>
/// The author is trusted and may write raw HTML, so this is defense in depth: it keeps everything a post
/// legitimately renders (heading ids, footnotes, task-list checkboxes, figures, classes from
/// <c>{.class}</c> hints, the external-link icon) while stopping a compromised admin account from storing
/// script. Iframes are kept only when they point at YouTube or Vimeo; form controls that could post data
/// elsewhere (phishing) are removed.
/// </para>
/// <para>
/// Uses the HtmlSanitizer (Ganss.Xss) package. Its instances are thread-safe once configured, so this class
/// is registered as a singleton.
/// </para>
/// </remarks>
public sealed class PostHtmlSanitizer
{
    /// <summary>Hosts whose iframes may appear in posts: the media-link embeds (design 10.1).</summary>
    private static readonly HashSet<string> AllowedFrameHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.youtube-nocookie.com",
        "www.youtube.com",
        "player.vimeo.com"
    };

    private readonly HtmlSanitizer sanitizer;

    /// <summary>Configures the post allowlist on top of HtmlSanitizer's defaults.</summary>
    public PostHtmlSanitizer()
    {
        sanitizer = new HtmlSanitizer();

        // Media embeds and responsive images (the defaults already cover figure, input, details, mark, kbd).
        sanitizer.AllowedTags.UnionWith(["iframe", "picture", "source"]);

        // Posts never need forms; allowing them would let stored HTML collect data for another site.
        sanitizer.AllowedTags.ExceptWith(["form", "button", "textarea", "select", "option", "optgroup", "keygen", "datalist"]);
        sanitizer.AllowedAttributes.ExceptWith(["action", "formaction", "method", "enctype"]);

        // The defaults omit class and id, which the pipeline relies on for anchors, footnotes, task lists
        // and styling hints.
        sanitizer.AllowedAttributes.UnionWith(
        [
            "class", "id", "allowfullscreen", "frameborder", "loading", "decoding", "srcset", "sizes",
            "aria-hidden", "aria-label", "role"
        ]);

        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.PostProcessDom += (_, e) => RemoveForeignFrames(e.Document);
    }

    /// <summary>Returns <paramref name="html"/> with anything outside the post allowlist removed.</summary>
    public string Sanitize(string? html)
    {
        return string.IsNullOrEmpty(html) ? string.Empty : sanitizer.Sanitize(html);
    }

    /// <summary>
    /// Returns <paramref name="html"/> sanitized like <see cref="Sanitize(string?)"/>, with relative URLs made
    /// absolute against <paramref name="baseUrl"/>, for HTML that leaves the site, such as feed items (design 16).
    /// </summary>
    /// <param name="html">The HTML to sanitize.</param>
    /// <param name="baseUrl">
    /// The absolute URL of the page the HTML belongs to, so both root-relative links (<c>/media/…</c>) and
    /// fragment links (footnotes, <c>#fn1</c>) keep pointing at the right place.
    /// </param>
    public string Sanitize(string? html, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        return string.IsNullOrEmpty(html) ? string.Empty : sanitizer.Sanitize(html, baseUrl.AbsoluteUri);
    }

    /// <summary>Removes iframes whose source isn't an https URL on an allowed video host.</summary>
    private static void RemoveForeignFrames(AngleSharp.Html.Dom.IHtmlDocument document)
    {
        foreach (var frame in document.QuerySelectorAll("iframe").ToList())
        {
            var src = frame.GetAttribute("src");

            // Protocol-relative embeds ("//www.youtube.com/…") are read as https.
            var isAllowed = src is not null
                && Uri.TryCreate(src.StartsWith("//", StringComparison.Ordinal) ? "https:" + src : src, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && AllowedFrameHosts.Contains(uri.Host);

            if (!isAllowed)
            {
                frame.Remove();
            }
        }
    }
}