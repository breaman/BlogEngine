namespace BlogEngine.Shared.Common;

/// <summary>
/// Fixed public URL paths of the site-wide feeds and SEO files (design 7.1, 16).
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

    /// <summary>Site search (wired in T4.9); the navbar's GET form already submits here.</summary>
    public const string Search = "/search";
}
