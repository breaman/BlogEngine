using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the year, month and day archives (design 7.1, P2, P4, T1.20): each level lists its posts with a heading
/// and breadcrumbs, accepts unpadded numbers but links with two digits, and answers 404 for an empty period or an
/// impossible date.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class ArchivePagesTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Every level shows the post, the period heading and breadcrumbs to the parent periods.</summary>
    [Test]
    public async Task EachLevel_ListsPostsWithHeadingAndBreadcrumbs()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Archived {token}" }, PublicTestPosts.Noon(year, 9, 22));
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Other day {token}" }, PublicTestPosts.Noon(year, 9, 3));
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Other month {token}" }, PublicTestPosts.Noon(year, 10, 1));
        using var client = IdentityTestHelper.CreateClient(factory);

        var yearHtml = await PublicTestPosts.GetOkAsync(client, $"/posts/{year}");
        var monthHtml = await PublicTestPosts.GetOkAsync(client, $"/posts/{year}/09");
        var dayHtml = await PublicTestPosts.GetOkAsync(client, $"/posts/{year}/09/22");

        await Assert.That(yearHtml).Contains($"Posts from {year}</h1>");
        await Assert.That(yearHtml).Contains($"Archived {token}");
        await Assert.That(yearHtml).Contains($"Other month {token}");

        await Assert.That(monthHtml).Contains($"Posts from September {year}</h1>");
        await Assert.That(monthHtml).Contains($"Archived {token}");
        await Assert.That(monthHtml).Contains($"Other day {token}");
        await Assert.That(monthHtml).DoesNotContain($"Other month {token}");
        await Assert.That(monthHtml).Contains($"<a href=\"/posts/{year}\">{year}</a>");

        await Assert.That(dayHtml).Contains($"Posts from September 22, {year}</h1>");
        await Assert.That(dayHtml).Contains($"Archived {token}");
        await Assert.That(dayHtml).DoesNotContain($"Other day {token}");
        await Assert.That(dayHtml).Contains("<a href=\"/posts\">Posts</a>");
        await Assert.That(dayHtml).Contains($"<a href=\"/posts/{year}/09\">September</a>");
        await Assert.That(dayHtml).Contains($"<link rel=\"canonical\" href=\"http://localhost/posts/{year}/09/22\"");
    }

    /// <summary>Unpadded months and days work, and the canonical URL and links still use two digits.</summary>
    [Test]
    public async Task UnpaddedNumbers_AreAcceptedButLinkedPadded()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Padded {token}" }, PublicTestPosts.Noon(year, 3, 4));
        using var client = IdentityTestHelper.CreateClient(factory);

        var monthHtml = await PublicTestPosts.GetOkAsync(client, $"/posts/{year}/3");
        var dayHtml = await PublicTestPosts.GetOkAsync(client, $"/posts/{year}/3/4");

        await Assert.That(monthHtml).Contains($"Padded {token}");
        await Assert.That(monthHtml).Contains($"<link rel=\"canonical\" href=\"http://localhost/posts/{year}/03\"");
        await Assert.That(dayHtml).Contains($"<link rel=\"canonical\" href=\"http://localhost/posts/{year}/03/04\"");
        await Assert.That(dayHtml).Contains($"<a href=\"/posts/{year}/03\">March</a>");
    }

    /// <summary>A real period with no visible posts is 404 at every level, even when it has a draft.</summary>
    [Test]
    public async Task EmptyPeriod_Returns404()
    {
        var year = PublicTestPosts.NextYear();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Unpublished {PublicTestPosts.Token()}" },
            PublicTestPosts.Noon(year, 7, 7));
        await PublicTestPosts.SaveAsync(factory, s => s.UnpublishAsync(post.Id, new UnpublishPostRequest()));
        using var client = IdentityTestHelper.CreateClient(factory);

        foreach (var path in new[] { $"/posts/{year}", $"/posts/{year}/07", $"/posts/{year}/07/07", $"/posts/{PublicTestPosts.NextYear()}/01/01" })
        {
            using var response = await client.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound).Because(path);
        }
    }

    /// <summary>Impossible dates and years outside the archive range are 404.</summary>
    [Test]
    [Arguments("/posts/2026/02/30")]
    [Arguments("/posts/2025/02/29")]
    [Arguments("/posts/2026/13")]
    [Arguments("/posts/2026/00")]
    [Arguments("/posts/2026/09/32")]
    [Arguments("/posts/2026/09/00")]
    [Arguments("/posts/1899")]
    [Arguments("/posts/10000")]
    [Arguments("/posts/abcd")]
    public async Task InvalidDate_Returns404(string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>A page past the end of an archive is 404.</summary>
    [Test]
    public async Task PagePastEnd_Returns404()
    {
        var year = PublicTestPosts.NextYear();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Single {PublicTestPosts.Token()}" }, PublicTestPosts.Noon(year, 1, 1));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(PostPaths.Year(year) + "?page=2");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}
