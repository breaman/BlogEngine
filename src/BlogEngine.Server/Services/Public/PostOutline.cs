using System.ComponentModel;
using System.Text.RegularExpressions;

using AngleSharp.Html.Parser;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Builds a post's table of contents (design 14.2, P13) from its rendered HTML: the <c>h2</c> headings, each with the
/// <c>h3</c> headings under it, linked by the ids the Markdown pipeline gave them.
/// </summary>
/// <remarks>
/// <para>
/// Read from the stored, sanitized HTML rather than the Markdown, so the entries match exactly what the page shows,
/// including headings written as raw HTML and ids set with <c>{#id}</c>. The HTML is parsed with AngleSharp, which
/// comes with the HtmlSanitizer package.
/// </para>
/// <para>
/// Short posts don't need a table of contents, so one is only built when the post has at least
/// <see cref="MinimumSections"/> <c>h2</c> headings.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var outline = PostOutline.FromHtml(post.ContentHtml); // empty for fewer than four h2 headings
/// </code>
/// </example>
public static partial class PostOutline
{
    /// <summary>The number of <c>h2</c> headings from which a post gets a table of contents (P13).</summary>
    public const int MinimumSections = 4;

    /// <summary>The entries of the table of contents, or none when the post has too few sections.</summary>
    public static IReadOnlyList<OutlineHeading> FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var document = new HtmlParser().ParseDocument(html);
        var headings = document.QuerySelectorAll("h2[id], h3[id]")
            .Select(h => (Level: h.LocalName == "h2" ? 2 : 3, Id: h.Id!, Text: CollapseWhitespace(h.TextContent)))
            .Where(h => h.Id.Length > 0 && h.Text.Length > 0)
            .ToList();

        if (headings.Count(h => h.Level == 2) < MinimumSections)
        {
            return [];
        }

        // Each h3 belongs to the h2 before it; one that comes before any h2 stands on its own.
        var entries = new List<(string Id, string Text, List<OutlineHeading> Children)>();
        foreach (var heading in headings)
        {
            if (heading.Level == 3 && entries.Count > 0)
            {
                entries[^1].Children.Add(new OutlineHeading(heading.Id, heading.Text, []));
            }
            else
            {
                entries.Add((heading.Id, heading.Text, []));
            }
        }

        return [.. entries.Select(e => new OutlineHeading(e.Id, e.Text, e.Children))];
    }

    /// <summary>Joins runs of whitespace (line breaks inside a heading) into single spaces.</summary>
    private static string CollapseWhitespace(string text)
    {
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>One entry of a post's table of contents.</summary>
/// <remarks>Immutable, like the other cached public records, so <c>HybridCache</c> can share one instance.</remarks>
/// <param name="Id">The heading's id, the target of the <c>#fragment</c> link.</param>
/// <param name="Text">The heading's text.</param>
/// <param name="Children">The <c>h3</c> headings under an <c>h2</c>.</param>
[ImmutableObject(true)]
public sealed record OutlineHeading(string Id, string Text, IReadOnlyList<OutlineHeading> Children);
