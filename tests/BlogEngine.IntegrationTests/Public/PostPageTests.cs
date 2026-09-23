using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the post page at <c>/posts/{yyyy}/{mm}/{dd}/{slug}</c> (design 7.1, 14.2, P1, T1.21): the post renders at
/// its canonical URL, other URLs for it redirect permanently, moved posts follow the redirect table, and drafts and
/// unknown slugs are 404.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PostPageTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The canonical URL shows the title, content, linked date, reading time, tags and share links.</summary>
    [Test]
    public async Task CanonicalUrl_RendersPost()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Rendered post {token}",
            ContentMarkdown = $"Some **bold** words about {token}.",
            Tags = [$"Render-{token}"]
        }, PublicTestPosts.Noon(year, 3, 7));
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains($"<h1 class=\"post-title mb-2\">Rendered post {token}</h1>");
        await Assert.That(html).Contains($"<strong>bold</strong> words about {token}.");
        await Assert.That(html).Contains($"href=\"/posts/{year}/03/07\"");
        await Assert.That(html).Contains($"datetime=\"{year}-03-07\"");
        await Assert.That(html).Contains("min read");
        await Assert.That(html).Contains($"href=\"/tags/render-{token}\"");
        await Assert.That(html).Contains("https://bsky.app/intent/compose?text=");
        await Assert.That(html).Contains($"<link rel=\"canonical\" href=\"http://localhost{post.PublicPath}\"");
        await Assert.That(html).Contains($"<title>Rendered post {token} | ");
    }

    /// <summary>A wrong date, an unpadded date or an upper-case slug answers 301 to the canonical URL.</summary>
    [Test]
    public async Task NonCanonicalUrl_Returns301ToCanonical()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Canonical {token}" },
            PublicTestPosts.Noon(year, 9, 5));
        using var client = IdentityTestHelper.CreateClient(factory);

        foreach (var path in new[]
                 {
                     $"/posts/{year}/01/01/{post.Slug}",
                     $"/posts/{year}/9/5/{post.Slug}",
                     $"/posts/{year}/09/05/{post.Slug!.ToUpperInvariant()}"
                 })
        {
            using var response = await client.GetAsync(path);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
            await Assert.That(response.Headers.Location!.ToString()).IsEqualTo(post.PublicPath);
        }
    }

    /// <summary>After a slug change, the old URL answers 301 from the redirect table.</summary>
    [Test]
    public async Task MovedPost_Returns301FromRedirectTable()
    {
        var token = PublicTestPosts.Token();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Moving {token}" },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 4, 1));
        var oldPath = post.PublicPath!;
        post.Slug = $"moved-{token}";
        var moved = await PublicTestPosts.SaveAsync(factory, s => s.UpdateAsync(post.Id, post));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(oldPath + "?utm=feed");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo(moved.PublicPath + "?utm=feed");
        await Assert.That(await PublicTestPosts.GetOkAsync(client, moved.PublicPath!)).Contains($"Moving {token}");
    }

    /// <summary>Drafts, unpublished and trashed posts are 404 at the URL they would have.</summary>
    [Test]
    public async Task NonVisiblePosts_Return404()
    {
        var token = PublicTestPosts.Token();
        var year = PublicTestPosts.NextYear();
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Draft {token}" });
        var unpublished = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Unpublished {token}" },
            PublicTestPosts.Noon(year, 5, 5));
        var trashed = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Trashed {token}" },
            PublicTestPosts.Noon(year, 5, 6));
        using var client = IdentityTestHelper.CreateClient(factory);

        // Visible first, so the cached pages have to be evicted for the 404s below.
        await PublicTestPosts.GetOkAsync(client, unpublished.PublicPath!);
        await PublicTestPosts.GetOkAsync(client, trashed.PublicPath!);
        await PublicTestPosts.SaveAsync(factory, s => s.UnpublishAsync(unpublished.Id, new UnpublishPostRequest()));
        await PublicTestPosts.TrashAsync(factory, trashed.Id);

        foreach (var path in new[] { PostPaths.Post(new DateOnly(year, 5, 4), draft.Slug!), unpublished.PublicPath!, trashed.PublicPath! })
        {
            using var response = await client.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    /// <summary>An unknown slug is 404, rendered with the site's not-found page.</summary>
    [Test]
    public async Task UnknownSlug_Returns404()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync($"/posts/2026/09/22/no-such-post-{PublicTestPosts.Token()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Not Found");
    }
}
