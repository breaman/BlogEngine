using System.Globalization;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Tags on the <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> and output cache entries of
/// the public site (design 11), so a change evicts every entry that could show it.
/// </summary>
public static class PublicCacheTags
{
    /// <summary>Everything derived from the visible posts: lists, archives, tags, post pages, feeds, sitemap.</summary>
    public const string Posts = "posts";

    /// <summary>Standalone pages (design 6.7, A17): the page snapshot behind the navigation, and each page's content.</summary>
    public const string Pages = "pages";

    /// <summary>Output cache entries that depend only on the site settings, such as <c>robots.txt</c>.</summary>
    public const string Settings = "settings";

    /// <summary>Entries that show one post, such as its rendered content.</summary>
    public static string Post(int postId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"post:{postId}");
    }

    /// <summary>Entries scoped to one tag, such as its feed.</summary>
    public static string Tag(int tagId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"tag:{tagId}");
    }

    /// <summary>The approved comments of one post (design 11), evicted on submission and moderation.</summary>
    public static string Comments(int postId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"comments:{postId}");
    }
}