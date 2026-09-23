using System.Globalization;
using System.Net;
using System.Xml.Linq;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests <c>/sitemap.xml</c> (design 16, P7, T1.25): the home page, <c>/posts</c>, every visible post with its
/// <c>lastmod</c>, and tag pages with visible posts; drafts and draft-only tags are left out.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class SitemapTests(BlogEngineWebApplicationFactory factory)
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>The sitemap lists the fixed pages, the visible post with its publish time and its tag page.</summary>
    [Test]
    public async Task Sitemap_ListsVisibleContent()
    {
        var token = PublicTestPosts.Token();
        var publishedOn = PublicTestPosts.Noon(PublicTestPosts.NextYear(), 4, 30);
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Mapped {token}", Tags = [$"Mapped-{token}"] }, publishedOn);
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Unmapped {token}", Tags = [$"Unmapped-{token}"] });
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(SitePaths.Sitemap);
        var urls = XDocument.Parse(await response.Content.ReadAsStringAsync())
            .Root!.Elements(Sitemap + "url")
            .ToDictionary(u => u.Element(Sitemap + "loc")!.Value, u => u.Element(Sitemap + "lastmod")?.Value);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/xml");
        await Assert.That(urls.Keys).Contains("http://localhost/");
        await Assert.That(urls.Keys).Contains("http://localhost/posts");
        await Assert.That(urls[$"http://localhost{post.PublicPath}"])
            .IsEqualTo(publishedOn.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        await Assert.That(urls.Keys).Contains($"http://localhost/tags/mapped-{token}");
        await Assert.That(urls.Keys.Any(u => u.EndsWith(draft.Slug!, StringComparison.Ordinal))).IsFalse();
        await Assert.That(urls.Keys).DoesNotContain($"http://localhost/tags/unmapped-{token}");
    }
}
