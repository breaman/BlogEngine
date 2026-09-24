using System.Globalization;

namespace BlogEngine.Shared.Common;

/// <summary>
/// Builds the canonical public URL paths of posts and their date archives (design 7.1, 16).
/// </summary>
/// <remarks>
/// Defined once so redirects, admin "view" links, list pages, feeds and the sitemap all agree on the canonical
/// form: lowercase, zero-padded month and day, no trailing slash. The archive routes also accept unpadded
/// numbers (<c>/posts/2026/9</c>), but links are always built here, with two digits.
/// </remarks>
/// <example>
/// <code>
/// PostPaths.Post(new DateOnly(2026, 9, 2), "hello-world"); // "/posts/2026/09/02/hello-world"
/// PostPaths.Month(2026, 9);                                 // "/posts/2026/09"
/// </code>
/// </example>
public static class PostPaths
{
    /// <summary>The post index, newest first.</summary>
    public const string Index = "/posts";

    /// <summary>The canonical path of a post published on <paramref name="publishedDateLocal"/>.</summary>
    /// <param name="publishedDateLocal">The publish date in the blog's time zone.</param>
    /// <param name="slug">The post slug.</param>
    public static string Post(DateOnly publishedDateLocal, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return string.Create(CultureInfo.InvariantCulture, $"{Day(publishedDateLocal)}/{slug}");
    }

    /// <summary>The archive of every post published in <paramref name="year"/>.</summary>
    public static string Year(int year)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Index}/{year:D4}");
    }

    /// <summary>The archive of one month, with the month zero-padded.</summary>
    public static string Month(int year, int month)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Year(year)}/{month:D2}");
    }

    /// <summary>The archive of one day, with the month and day zero-padded.</summary>
    public static string Day(DateOnly date)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Month(date.Year, date.Month)}/{date.Day:D2}");
    }

    /// <summary>
    /// <paramref name="path"/> for list page <paramref name="page"/>: page 1 adds nothing, so it has one canonical URL
    /// (design 16). A path that already has a query string, such as a search, gets <c>&amp;page=</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// PostPaths.WithPage("/posts", 1);           // "/posts"
    /// PostPaths.WithPage("/posts", 3);           // "/posts?page=3"
    /// PostPaths.WithPage("/search?q=blazor", 2); // "/search?q=blazor&amp;page=2"
    /// </code>
    /// </example>
    public static string WithPage(string path, int page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return page <= 1 ? path : string.Create(CultureInfo.InvariantCulture, $"{path}{separator}page={page}");
    }
}
