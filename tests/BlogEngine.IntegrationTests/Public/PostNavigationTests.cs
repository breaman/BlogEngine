using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the links under a post (design 14.2, P10, T4.11): the older and newer posts by publish date, and related posts
/// sharing its tags. The first/last-post boundaries are covered by the <c>PublicPostIndex</c> unit tests, since the
/// session's shared database always has other tests' posts around these.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PostNavigationTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>
    /// The middle of three consecutive posts links to the one before it (rel=prev) and after it (rel=next). They are in a
    /// year of their own, so no other test's post falls between them.
    /// </summary>
    [Test]
    public async Task MiddlePost_LinksToOlderAndNewer()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        var older = await PublishAsync($"Older {token}", year, 2, []);
        var middle = await PublishAsync($"Middle {token}", year, 3, []);
        var newer = await PublishAsync($"Newer {token}", year, 4, []);
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, middle.PublicPath!);

        await Assert.That(html).Contains($"href=\"{older.PublicPath}\" rel=\"prev\"");
        await Assert.That(html).Contains($"Older {token}</span>");
        await Assert.That(html).Contains($"href=\"{newer.PublicPath}\" rel=\"next\"");
        await Assert.That(html).Contains($"Newer {token}</span>");
    }

    /// <summary>Related posts share tags, the post sharing the most tags first; unrelated posts and the post itself aren't listed.</summary>
    [Test]
    public async Task RelatedPosts_ShareTags_MostSharedFirst()
    {
        var token = PublicTestPosts.Token();
        var (a, b) = ($"RelA-{token}", $"RelB-{token}");
        var year = PublicTestPosts.NextYear();
        var oneTag = await PublishAsync($"One tag {token}", year, 5, [a]);
        var twoTags = await PublishAsync($"Two tags {token}", year, 1, [a, b]);
        var unrelated = await PublishAsync($"Unrelated {token}", year, 6, [$"Other-{token}"]);
        var post = await PublishAsync($"Subject {token}", year, 7, [a, b]);
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        var related = html[html.IndexOf("id=\"related-posts-heading\"", StringComparison.Ordinal)..];

        await Assert.That(related).Contains($"href=\"{twoTags.PublicPath}\"");
        await Assert.That(related).Contains($"href=\"{oneTag.PublicPath}\"");
        await Assert.That(related.IndexOf(twoTags.PublicPath!, StringComparison.Ordinal))
            .IsLessThan(related.IndexOf(oneTag.PublicPath!, StringComparison.Ordinal));
        await Assert.That(related).DoesNotContain(unrelated.PublicPath!);
        await Assert.That(related).DoesNotContain($"href=\"{post.PublicPath}\"");
    }

    /// <summary>A post without tags has no related posts section.</summary>
    [Test]
    public async Task UntaggedPost_HasNoRelatedPosts()
    {
        var post = await PublishAsync($"Lonely {PublicTestPosts.Token()}", PublicTestPosts.NextYear(), 1, []);
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).DoesNotContain("related-posts-heading");
    }

    private Task<PostEditDto> PublishAsync(string title, int year, int month, List<string> tags)
    {
        return PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = title, Tags = tags }, PublicTestPosts.Noon(year, month, 1));
    }
}
