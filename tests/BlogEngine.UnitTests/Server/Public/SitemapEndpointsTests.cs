using System.Text;
using System.Xml.Linq;

using BlogEngine.Server.Endpoints;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests the sitemap and <c>robots.txt</c> builders in <see cref="SitemapEndpoints"/> (design 16).
/// </summary>
public class SitemapEndpointsTests
{
    private static readonly Uri SiteUrl = new("https://blog.example/");
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>
    /// The sitemap has the fixed pages, each post with its last change, each tag page, and each standalone page with its
    /// last save.
    /// </summary>
    [Test]
    public async Task BuildSitemap_ListsPagesPostsAndTags()
    {
        var updated = PublicTestData.Post(2, new DateOnly(2026, 9, 20)) with { LastUpdatedOn = new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.FromHours(-5)) };
        var index = new PublicPostIndex([PublicTestData.Post(1), updated], [new PublicTag(3, "C#", "csharp", null, 2)]);
        var pages = new PublicPageIndex([new PublicPageSummary(4, "About", "about", true, 0, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero))]);

        var urls = XDocument.Parse(Encoding.UTF8.GetString(SitemapEndpoints.BuildSitemap(index, pages, SiteUrl)))
            .Root!.Elements(Sitemap + "url")
            .ToDictionary(u => u.Element(Sitemap + "loc")!.Value, u => u.Element(Sitemap + "lastmod")?.Value);

        await Assert.That(urls.Keys).IsEquivalentTo(
        [
            "https://blog.example/",
            "https://blog.example/posts",
            "https://blog.example/archive",
            "https://blog.example/posts/2026/09/22/post-1",
            "https://blog.example/posts/2026/09/20/post-2",
            "https://blog.example/tags/csharp",
            "https://blog.example/about"
        ]);
        await Assert.That(urls["https://blog.example/about"]).IsEqualTo("2026-09-01T09:00:00Z");
        await Assert.That(urls["https://blog.example/posts/2026/09/22/post-1"]).IsEqualTo("2026-09-22T12:00:00Z");
        await Assert.That(urls["https://blog.example/posts/2026/09/20/post-2"]).IsEqualTo("2026-09-21T13:30:00Z");
        await Assert.That(urls["https://blog.example/"]).IsEqualTo("2026-09-22T12:00:00Z");
        await Assert.That(urls["https://blog.example/tags/csharp"]).IsNull();
    }

    /// <summary>By default the private areas are disallowed and the sitemap is listed.</summary>
    [Test]
    public async Task BuildRobots_Default()
    {
        var robots = SitemapEndpoints.BuildRobots(new SiteSettingsDto(), SiteUrl);

        await Assert.That(robots).IsEqualTo(
            "User-agent: *\nDisallow: /admin\nDisallow: /api\nDisallow: /preview\nDisallow: /Account\n\nSitemap: https://blog.example/sitemap.xml\n");
    }

    /// <summary>"Discourage search engines" disallows everything; extras are appended with normalized line endings.</summary>
    [Test]
    public async Task BuildRobots_DiscourageSearchEngines()
    {
        var robots = SitemapEndpoints.BuildRobots(
            new SiteSettingsDto { DiscourageSearchEngines = true, RobotsTxtExtras = "User-agent: GPTBot\r\nDisallow: /\r\n" }, SiteUrl);

        await Assert.That(robots).IsEqualTo(
            "User-agent: *\nDisallow: /\n\nUser-agent: GPTBot\nDisallow: /\n\nSitemap: https://blog.example/sitemap.xml\n");
    }
}
