namespace BlogEngine.Shared.Common;

/// <summary>
/// Words a standalone page can't use as its slug (design 7.1, A17), because <c>/{pageSlug}</c> would collide with a
/// route the site already has, or one planned for it.
/// </summary>
/// <remarks>
/// Pages have the lowest route priority, so a page named like a real route would simply never be reached; rejecting
/// the slug up front says so instead. Names with a dot (<c>feed.xml</c>, <c>robots.txt</c>) can't be slugs anyway, but
/// they are listed so the list documents every top-level path.
/// </remarks>
/// <example>
/// <code>
/// ReservedSlugs.IsReserved("admin");  // true
/// ReservedSlugs.IsReserved("about");  // false
/// </code>
/// </example>
public static class ReservedSlugs
{
    /// <summary>The reserved top-level path segments, lowercase.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Public routes (design 7.1).
        "posts", "tags", "archive", "search", "preview", "media",
        "feed.xml", "atom.xml", "sitemap.xml", "robots.txt",
        // Admin, API, accounts and setup.
        "admin", "api", "account", "setup",
        // Framework and infrastructure paths.
        "not-found", "error", "health", "alive", "_framework", "_content", "_blazor",
        // Static asset folders and files in wwwroot.
        "css", "js", "fonts", "lib", "favicon.png", "favicon.ico"
    };

    /// <summary>Whether <paramref name="slug"/> is reserved (compared case-insensitively).</summary>
    public static bool IsReserved(string? slug)
    {
        return slug is not null && All.Contains(slug.Trim());
    }
}