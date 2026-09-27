using System.Text.RegularExpressions;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Finds the media library items a post's Markdown refers to, for <c>PostMedia</c> usage tracking
/// (design 6.6, 9.4).
/// </summary>
/// <remarks>
/// Library URLs have the shape <c>/media/{publicId}/{fileName}</c>, relative or absolute. The raw Markdown
/// is scanned rather than the parsed links so references in raw HTML (<c>&lt;img src&gt;</c>, <c>&lt;a href&gt;</c>)
/// count too; an over-match only makes an item look used, which is the safe direction for deletion checks.
/// </remarks>
/// <example>
/// <code>
/// var ids = MediaReferenceScanner.FindPublicIds("![Sunset](/media/ab12cd34ef56/sunset.jpg)"); // ["ab12cd34ef56"]
/// </code>
/// </example>
public static partial class MediaReferenceScanner
{
    /// <summary>Distinct media public ids referenced by <paramref name="markdown"/>, in order of first use.</summary>
    public static IReadOnlyList<string> FindPublicIds(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        return [.. MediaUrlRegex().Matches(markdown).Select(m => m.Groups["id"].Value).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <c>/media/</c> not preceded by a path or word character (so <c>/social/media/…</c> doesn't match),
    /// then a 12-character public id and a slash.
    /// </summary>
    [GeneratedRegex(@"(?<![\w/.-])/media/(?<id>[A-Za-z0-9_-]{12})/|(?<=://[^/\s""'()<>]+)/media/(?<id>[A-Za-z0-9_-]{12})/", RegexOptions.CultureInvariant)]
    private static partial Regex MediaUrlRegex();
}