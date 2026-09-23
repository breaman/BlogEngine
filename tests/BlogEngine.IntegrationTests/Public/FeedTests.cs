using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the RSS and Atom feeds (design 16, P6, T1.24): they parse as valid feeds, carry the latest visible posts
/// with full HTML and absolute URLs and tags as categories, never include drafts, and a publish shows up
/// straight away despite the output cache.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class FeedTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The RSS feed has the post with its absolute link, categories and full HTML with absolute URLs.</summary>
    [Test]
    public async Task RssFeed_HasVisiblePostsWithAbsoluteUrls()
    {
        var token = PublicTestPosts.Token();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Feed post {token}",
            ContentMarkdown = $"See [all posts](/posts) for {token}.[^1]\n\n[^1]: A footnote.",
            Tags = [$"Feed-{token}"]
        });
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Feed draft {token}" });
        using var client = IdentityTestHelper.CreateClient(factory);

        var (response, feed) = await LoadAsync(client, SitePaths.RssFeed);

        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/rss+xml");
        var item = feed.Items.SingleOrDefault(i => i.Title.Text == $"Feed post {token}");
        await Assert.That(item).IsNotNull();
        await Assert.That(item!.Links.Single().Uri.AbsoluteUri).IsEqualTo($"http://localhost{post.PublicPath}");
        await Assert.That(item.Categories.Select(c => c.Name)).Contains($"Feed-{token}");
        var html = ((TextSyndicationContent)item.Summary).Text;
        await Assert.That(html).Contains("href=\"http://localhost/posts\"");
        await Assert.That(html).Contains($"href=\"http://localhost{post.PublicPath}#fn:1\"");
        await Assert.That(feed.Items.Any(i => i.Title.Text == $"Feed draft {token}")).IsFalse();
        await Assert.That(feed.Items.Count()).IsLessThanOrEqualTo(20);
    }

    /// <summary>The Atom feed has the post with HTML content, a summary, and a feed author.</summary>
    [Test]
    public async Task AtomFeed_HasVisiblePosts()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Atom post {token}", ContentMarkdown = $"**Bold** {token}." });
        using var client = IdentityTestHelper.CreateClient(factory);

        var (response, feed) = await LoadAsync(client, SitePaths.AtomFeed);

        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/atom+xml");
        await Assert.That(feed.Authors).IsNotEmpty();
        var item = feed.Items.SingleOrDefault(i => i.Title.Text == $"Atom post {token}");
        await Assert.That(item).IsNotNull();
        await Assert.That(((TextSyndicationContent)item!.Content).Text).Contains($"<strong>Bold</strong> {token}.");
        await Assert.That(item.Summary.Text).IsEqualTo($"Bold {token}.");
    }

    /// <summary>A tag's feed has only that tag's visible posts.</summary>
    [Test]
    public async Task TagFeed_HasOnlyTaggedPosts()
    {
        var token = PublicTestPosts.Token();
        var tag = $"Tagfeed-{token}";
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Tagged {token}", Tags = [tag] });
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Untagged {token}" });
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Tagged draft {token}", Tags = [tag] });
        using var client = IdentityTestHelper.CreateClient(factory);

        var (_, feed) = await LoadAsync(client, TagPaths.Feed($"tagfeed-{token}"));

        await Assert.That(feed.Items.Select(i => i.Title.Text)).IsEquivalentTo([$"Tagged {token}"]);
        await Assert.That(feed.Title.Text).EndsWith($": {tag}");
    }

    /// <summary>A post published after the feed was cached is in the next response: publishing evicts the output cache.</summary>
    [Test]
    public async Task Publish_EvictsCachedFeed()
    {
        var token = PublicTestPosts.Token();
        using var client = IdentityTestHelper.CreateClient(factory);
        await LoadAsync(client, SitePaths.RssFeed);
        await LoadAsync(client, SitePaths.AtomFeed);

        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Fresh {token}" });
        var (_, rss) = await LoadAsync(client, SitePaths.RssFeed);
        var (_, atom) = await LoadAsync(client, SitePaths.AtomFeed);

        await Assert.That(rss.Items.Any(i => i.Title.Text == $"Fresh {token}")).IsTrue();
        await Assert.That(atom.Items.Any(i => i.Title.Text == $"Fresh {token}")).IsTrue();
    }

    /// <summary>GETs a feed, asserting a 200, and parses it (which fails for malformed RSS or Atom).</summary>
    private static async Task<(HttpResponseMessage Response, SyndicationFeed Feed)> LoadAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = XmlReader.Create(stream);
        return (response, SyndicationFeed.Load(reader));
    }
}
