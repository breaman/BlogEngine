using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the post index at <c>/posts</c> and the home page (design 7.1, P3, T1.19): newest first, paging with a
/// canonical first page, 404 past the end, and featured posts pinned above the latest ones.
/// </summary>
/// <remarks>
/// Other tests publish posts in parallel, so paging is checked in a year archive of this test's own (the same
/// list component), and <c>/posts</c> checks only what other posts can't disturb.
/// </remarks>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PostListPagesTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Posts published a moment apart appear newest first, with their date, reading time, tags and summary.</summary>
    /// <remarks>
    /// Posts that other tests publish meanwhile push these down the list, so the pages are read in order until
    /// both are found, and their positions are compared across the pages.
    /// </remarks>
    [Test]
    public async Task Posts_ListsNewestFirst()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Older {token}", Summary = $"Summary {token}", Tags = [$"List-{token}"] });
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Newer {token}" });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await ReadPagesUntilAsync(client, PostPaths.Index, $"Older {token}");

        await Assert.That(html.IndexOf($"Newer {token}", StringComparison.Ordinal)).IsGreaterThan(-1);
        await Assert.That(html.IndexOf($"Newer {token}", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf($"Older {token}", StringComparison.Ordinal));
        await Assert.That(html).Contains($"Summary {token}");
        await Assert.That(html).Contains($"href=\"/tags/list-{token}\"");
        await Assert.That(html).Contains("min read");
        await Assert.That(html).Contains("<link rel=\"canonical\" href=\"http://localhost/posts\"");
    }

    /// <summary><c>?page=1</c> is canonicalized to <c>/posts</c>; bad and out-of-range page numbers are 404.</summary>
    [Test]
    [Arguments("?page=0")]
    [Arguments("?page=-1")]
    [Arguments("?page=abc")]
    [Arguments("?page=100000")]
    [Arguments("?page=99999999999")]
    public async Task Posts_InvalidPage_Returns404(string query)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(PostPaths.Index + query);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>The first page's canonical URL has no query string.</summary>
    [Test]
    public async Task Posts_FirstPage_CanonicalOmitsPage()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, PostPaths.Index + "?page=1");

        await Assert.That(html).Contains("<link rel=\"canonical\" href=\"http://localhost/posts\"");
    }

    /// <summary>Twelve posts page as ten, then two, newest first, with pager links; page 3 is 404.</summary>
    [Test]
    public async Task List_PagesNewestFirst()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        for (var day = 1; day <= 12; day++)
        {
            await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Paged {token} day {day:D2}" },
                PublicTestPosts.Noon(year, 1, day));
        }

        using var client = IdentityTestHelper.CreateClient(factory);
        var yearPath = PostPaths.Year(year);

        var first = await PublicTestPosts.GetOkAsync(client, yearPath);
        var second = await PublicTestPosts.GetOkAsync(client, yearPath + "?page=2");
        using var third = await client.GetAsync(yearPath + "?page=3");

        await Assert.That(first).Contains($"Paged {token} day 12");
        await Assert.That(first).Contains($"Paged {token} day 03");
        await Assert.That(first).DoesNotContain($"Paged {token} day 02");
        await Assert.That(first.IndexOf("day 12", StringComparison.Ordinal)).IsLessThan(first.IndexOf("day 03", StringComparison.Ordinal));
        await Assert.That(first).Contains("Page 1 of 2");
        await Assert.That(first).Contains($"rel=\"next\" href=\"{yearPath}?page=2\"");
        await Assert.That(second).Contains($"Paged {token} day 02");
        await Assert.That(second).Contains($"Paged {token} day 01");
        await Assert.That(second).Contains("Page 2 of 2");
        await Assert.That(second).Contains($"rel=\"prev\" href=\"{yearPath}\"");
        await Assert.That(second).Contains($"<link rel=\"canonical\" href=\"http://localhost{yearPath}?page=2\"");
        await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A featured post from decades ago is pinned above the latest posts on the home page, which only shows
    /// "posts per page" of the newest posts otherwise.
    /// </summary>
    [Test]
    public async Task Home_PinsFeaturedPostAboveLatest()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Pinned {token}", IsFeatured = true },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 6, 1));
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, SitePaths.Home);

        await Assert.That(html.IndexOf($"Pinned {token}", StringComparison.Ordinal)).IsGreaterThan(-1);
        await Assert.That(html.IndexOf($"Pinned {token}", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf("id=\"latest-heading\"", StringComparison.Ordinal));
        await Assert.That(html).Contains("Featured</span>");
        await Assert.That(html).Contains("href=\"/posts\"");
    }

    /// <summary>The concatenated HTML of pages 1, 2, … of a list, up to the page containing <paramref name="text"/>.</summary>
    private static async Task<string> ReadPagesUntilAsync(HttpClient client, string path, string text)
    {
        var html = new System.Text.StringBuilder();
        for (var page = 1; ; page++)
        {
            using var response = await client.GetAsync(PostPaths.WithPage(path, page));
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"'{text}' wasn't on any page of {path}.");
            }

            var pageHtml = await response.Content.ReadAsStringAsync();
            html.Append(pageHtml);
            if (pageHtml.Contains(text, StringComparison.Ordinal))
            {
                return html.ToString();
            }
        }
    }
}