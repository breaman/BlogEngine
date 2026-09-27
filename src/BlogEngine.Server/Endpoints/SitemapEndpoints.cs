using System.Globalization;
using System.Text;
using System.Xml;

using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Http.HttpResults;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// <c>/sitemap.xml</c> and <c>/robots.txt</c> (design 7.1, 16, P7, T1.25).
/// </summary>
/// <remarks>
/// <para>
/// The sitemap lists the home page, <c>/posts</c>, the archive overview, every visible post (<c>lastmod</c> is the last
/// update, or else the publish time), every tag page with visible posts, and every published standalone page
/// (<c>lastmod</c> is its last save).
/// </para>
/// <para>
/// <c>robots.txt</c> keeps crawlers out of the admin, API, preview and account areas and points them at the
/// sitemap. With "discourage search engines" on it disallows everything instead, matching the sitewide
/// <c>noindex</c> meta tag. The author's <c>robots.txt</c> extras from the settings are appended.
/// </para>
/// <para>Both are output-cached and evicted by <see cref="CacheInvalidator"/>.</para>
/// </remarks>
public static class SitemapEndpoints
{
    /// <summary>Media type of the sitemap.</summary>
    public const string SitemapContentType = "application/xml; charset=utf-8";

    /// <summary>Media type of <c>robots.txt</c>.</summary>
    public const string RobotsContentType = "text/plain; charset=utf-8";

    /// <summary>Paths no crawler should visit (design 16).</summary>
    public static IReadOnlyList<string> DisallowedPaths { get; } = ["/admin", "/api", "/preview", "/Account"];

    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>Maps the sitemap and robots endpoints.</summary>
    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(SitePaths.Sitemap, WriteSitemapAsync).CacheOutput(PublicOutputCachePolicies.Posts);
        endpoints.MapGet(SitePaths.Robots, WriteRobotsAsync).CacheOutput(PublicOutputCachePolicies.Settings);

        return endpoints;
    }

    /// <summary>Writes the XML sitemap.</summary>
    private static async Task<FileContentHttpResult> WriteSitemapAsync(HttpRequest request, PublicPostQueries queries,
        PublicPageQueries pageQueries, CancellationToken cancellationToken)
    {
        var index = await queries.GetIndexAsync(cancellationToken);
        var pages = await pageQueries.GetIndexAsync(cancellationToken);
        return TypedResults.File(BuildSitemap(index, pages, PublicSiteUrl.Root(request)), SitemapContentType);
    }

    /// <summary>Writes <c>robots.txt</c> for the current settings.</summary>
    private static async Task<ContentHttpResult> WriteRobotsAsync(HttpRequest request, ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        return TypedResults.Text(BuildRobots(settings, PublicSiteUrl.Root(request)), RobotsContentType);
    }

    /// <summary>
    /// The sitemap XML for the posts and tags in <paramref name="index"/> and the standalone <paramref name="pages"/>,
    /// with absolute URLs under <paramref name="siteUrl"/>.
    /// </summary>
    public static byte[] BuildSitemap(PublicPostIndex index, PublicPageIndex pages, Uri siteUrl)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(siteUrl);

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", SitemapNamespace);

            DateTimeOffset? newest = index.Posts.Count > 0 ? index.Posts.Max(p => p.LastModified) : null;
            WriteUrl(writer, new Uri(siteUrl, SitePaths.Home), newest);
            WriteUrl(writer, new Uri(siteUrl, PostPaths.Index), newest);
            WriteUrl(writer, new Uri(siteUrl, SitePaths.Archive), newest);

            foreach (var post in index.Posts)
            {
                WriteUrl(writer, new Uri(siteUrl, post.Path), post.LastModified);
            }

            foreach (var tag in index.Tags)
            {
                WriteUrl(writer, new Uri(siteUrl, tag.Path), lastModified: null);
            }

            foreach (var page in pages.Pages)
            {
                WriteUrl(writer, new Uri(siteUrl, page.Path), page.ModifiedOn);
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>The <c>robots.txt</c> text for <paramref name="settings"/>.</summary>
    public static string BuildRobots(SiteSettingsDto settings, Uri siteUrl)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(siteUrl);

        var text = new StringBuilder();
        text.Append("User-agent: *\n");

        if (settings.DiscourageSearchEngines)
        {
            text.Append("Disallow: /\n");
        }
        else
        {
            foreach (var path in DisallowedPaths)
            {
                text.Append(CultureInfo.InvariantCulture, $"Disallow: {path}\n");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.RobotsTxtExtras))
        {
            text.Append('\n').Append(settings.RobotsTxtExtras.Trim().ReplaceLineEndings("\n")).Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"\nSitemap: {new Uri(siteUrl, SitePaths.Sitemap).AbsoluteUri}\n");

        return text.ToString();
    }

    /// <summary>Writes one <c>&lt;url&gt;</c> element.</summary>
    private static void WriteUrl(XmlWriter writer, Uri location, DateTimeOffset? lastModified)
    {
        writer.WriteStartElement("url", SitemapNamespace);
        writer.WriteElementString("loc", SitemapNamespace, location.AbsoluteUri);
        if (lastModified is { } modified)
        {
            // W3C datetime in UTC, as the sitemap protocol requires.
            writer.WriteElementString("lastmod", SitemapNamespace,
                modified.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        }

        writer.WriteEndElement();
    }
}