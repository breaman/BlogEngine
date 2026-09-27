using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the tag index and tag pages (design 7.1, P5, T1.22): only tags with visible posts are listed, with their
/// counts, and a tag page lists its visible posts or answers 404.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class TagPagesTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The index lists a tag with its count of visible posts; drafts don't count.</summary>
    [Test]
    public async Task TagIndex_ListsTagsWithVisibleCounts()
    {
        var token = PublicTestPosts.Token();
        var tag = $"Counted-{token}";
        var year = PublicTestPosts.NextYear();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"One {token}", Tags = [tag] }, PublicTestPosts.Noon(year, 1, 1));
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Two {token}", Tags = [tag] }, PublicTestPosts.Noon(year, 1, 2));
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Draft {token}", Tags = [tag] });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, TagPaths.Index);

        await Assert.That(html).Contains($"href=\"/tags/counted-{token}\"><span>#{tag}</span><span class=\"ms-1 text-body-secondary\">2</span>");
    }

    /// <summary>A tag used only by drafts isn't listed, and its page and feed are 404.</summary>
    [Test]
    public async Task DraftOnlyTag_IsHiddenAnd404s()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Draft {token}", Tags = [$"Hidden-{token}"] });
        using var client = IdentityTestHelper.CreateClient(factory);

        var index = await PublicTestPosts.GetOkAsync(client, TagPaths.Index);
        using var page = await client.GetAsync(TagPaths.Tag($"hidden-{token}"));
        using var feed = await client.GetAsync(TagPaths.Feed($"hidden-{token}"));

        await Assert.That(index).DoesNotContain($"hidden-{token}");
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(feed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>The tag page lists the tag's visible posts only, newest first, with a heading and the tag feed.</summary>
    [Test]
    public async Task TagPage_ListsVisiblePosts()
    {
        var token = PublicTestPosts.Token();
        var tag = $"Paged-{token}";
        var year = PublicTestPosts.NextYear();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Older {token}", Tags = [tag] }, PublicTestPosts.Noon(year, 2, 1));
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Newer {token}", Tags = [tag] }, PublicTestPosts.Noon(year, 2, 2));
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Untagged {token}" }, PublicTestPosts.Noon(year, 2, 3));
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Draft {token}", Tags = [tag] });
        using var client = IdentityTestHelper.CreateClient(factory);

        // Upper case in the URL still finds the tag; the canonical URL is the stored slug.
        var html = await PublicTestPosts.GetOkAsync(client, $"/tags/PAGED-{token}");

        await Assert.That(html).Contains($"Posts tagged <span class=\"text-body-secondary\">#</span>{tag}</h1>");
        await Assert.That(html).Contains("2 posts");
        await Assert.That(html.IndexOf($"Newer {token}", StringComparison.Ordinal)).IsGreaterThan(-1);
        await Assert.That(html.IndexOf($"Newer {token}", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf($"Older {token}", StringComparison.Ordinal));
        await Assert.That(html).DoesNotContain($"Untagged {token}");
        await Assert.That(html).DoesNotContain($"Draft {token}");
        await Assert.That(html).Contains($"<link rel=\"canonical\" href=\"http://localhost/tags/paged-{token}\"");
        await Assert.That(html).Contains($"href=\"http://localhost/tags/paged-{token}/feed.xml\"");
    }

    /// <summary>Unknown tags and pages past the end are 404.</summary>
    [Test]
    public async Task UnknownTagOrPage_Returns404()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Tagged {token}", Tags = [$"Short-{token}"] },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 1, 1));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var unknown = await client.GetAsync(TagPaths.Tag($"nothing-{token}"));
        using var pastEnd = await client.GetAsync(TagPaths.Tag($"short-{token}") + "?page=2");

        await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(pastEnd.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}