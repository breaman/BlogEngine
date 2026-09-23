using System.Globalization;

namespace BlogEngine.Shared.Common;

/// <summary>
/// Builds the canonical public URL paths of posts (design 7.1, 16).
/// </summary>
/// <remarks>
/// Defined once so redirects, admin "view" links, feeds and the sitemap all agree on the canonical form:
/// lowercase, zero-padded month and day, no trailing slash.
/// </remarks>
/// <example>
/// <code>
/// PostPaths.Post(new DateOnly(2026, 9, 2), "hello-world"); // "/posts/2026/09/02/hello-world"
/// </code>
/// </example>
public static class PostPaths
{
    /// <summary>The canonical path of a post published on <paramref name="publishedDateLocal"/>.</summary>
    /// <param name="publishedDateLocal">The publish date in the blog's time zone.</param>
    /// <param name="slug">The post slug.</param>
    public static string Post(DateOnly publishedDateLocal, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return string.Create(CultureInfo.InvariantCulture,
            $"/posts/{publishedDateLocal.Year:D4}/{publishedDateLocal.Month:D2}/{publishedDateLocal.Day:D2}/{slug}");
    }
}
