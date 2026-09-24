namespace BlogEngine.Shared.Common;

/// <summary>
/// Public URL paths of the site-wide pages, feeds and SEO files, and of standalone pages (design 7.1, 16).
/// </summary>
public static class SitePaths
{
    /// <summary>The home page.</summary>
    public const string Home = "/";

    /// <summary>The RSS 2.0 feed of the latest posts.</summary>
    public const string RssFeed = "/feed.xml";

    /// <summary>The Atom feed of the latest posts.</summary>
    public const string AtomFeed = "/atom.xml";

    /// <summary>The XML sitemap.</summary>
    public const string Sitemap = "/sitemap.xml";

    /// <summary>The robots exclusion file.</summary>
    public const string Robots = "/robots.txt";

    /// <summary>Site search (design 15, P9); the navbar's GET form submits here.</summary>
    public const string Search = "/search";

    /// <summary>The archive overview: every year and month with posts (design 7.1, P11).</summary>
    public const string Archive = "/archive";

    /// <summary>Prefix of private post previews, <c>/preview/{token}</c> (design 7.1, A14).</summary>
    public const string PreviewPrefix = "/preview";

    /// <summary>The public path of a standalone page, <c>/{slug}</c> (design 7.1, A17).</summary>
    public static string Page(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return $"/{slug}";
    }

    /// <summary>The private preview URL path for a preview token (design 7.1, A14).</summary>
    public static string Preview(string token)
    {
        return $"{PreviewPrefix}/{Uri.EscapeDataString(token)}";
    }
}
