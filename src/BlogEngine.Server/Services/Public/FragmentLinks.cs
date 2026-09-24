using AngleSharp.Html.Parser;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Points in-page links of rendered content (heading anchors, footnote references, a table of contents) at the page
/// they appear on: <c>href="#setup"</c> becomes <c>href="/posts/2026/09/22/hello#setup"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every page has <c>&lt;base href="/"&gt;</c> (the WebAssembly admin needs it), and a base element applies to fragment
/// links too: a bare <c>#setup</c> resolves to <c>/#setup</c>, the home page. With the page's path in front, the browser
/// (or Blazor's enhanced navigation) sees a link to the current page and just scrolls, with or without JavaScript.
/// </para>
/// <para>
/// The HTML is parsed with AngleSharp rather than searched as text, so a code sample that shows
/// <c>href="#…"</c> as text is left alone.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var html = FragmentLinks.Resolve(post.ContentHtml, post.Path);
/// </code>
/// </example>
public static class FragmentLinks
{
    /// <summary>
    /// <paramref name="html"/> with every <c>href</c> that starts with <c>#</c> prefixed by <paramref name="pagePath"/>.
    /// HTML without fragment links is returned unchanged.
    /// </summary>
    /// <param name="html">Rendered, sanitized content HTML.</param>
    /// <param name="pagePath">The site-relative path of the page showing it, such as <c>/about</c>.</param>
    public static string Resolve(string html, string pagePath)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        // Cheap pre-check: most posts have no fragment links, and parsing isn't free.
        if (!html.Contains("#", StringComparison.Ordinal))
        {
            return html;
        }

        var document = new HtmlParser().ParseDocument($"<body>{html}</body>");
        var links = document.Body!.QuerySelectorAll("[href^='#']");
        if (links.Length == 0)
        {
            return html;
        }

        foreach (var link in links)
        {
            link.SetAttribute("href", pagePath + link.GetAttribute("href"));
        }

        return document.Body.InnerHtml;
    }
}
