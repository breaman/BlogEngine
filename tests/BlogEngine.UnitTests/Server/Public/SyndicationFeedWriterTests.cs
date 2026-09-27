using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;

using BlogEngine.Server.Services;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="SyndicationFeedWriter"/>: RSS and Atom output with absolute URLs, categories, and the feed
/// content setting (design 16).
/// </summary>
public class SyndicationFeedWriterTests
{
    private static readonly Uri SiteUrl = new("https://blog.example/");
    private static readonly SyndicationFeedWriter Writer = new(new PostHtmlSanitizer());

    private static readonly PublicPostContent Post = PublicTestData.Content(
        PublicTestData.Post(1, new DateOnly(2026, 9, 22), new PublicTagLink(3, "C#", "csharp")),
        """<p>See <a href="/posts">all posts</a>.<a href="#fn:1" class="footnote-ref">1</a></p><script>alert(1)</script>""");

    /// <summary>RSS items carry the absolute link, the tag as a category and sanitized HTML with absolute URLs.</summary>
    [Test]
    public async Task Rss_FullContent_HasAbsoluteUrls()
    {
        var feed = Load(Writer.Write(SyndicationFeedWriter.Format.Rss, Settings(FeedContentMode.FullContent), [Post], SiteUrl,
            new Uri(SiteUrl, "/feed.xml")));

        var item = feed.Items.Single();
        var html = ((TextSyndicationContent)item.Summary).Text;
        await Assert.That(feed.Title.Text).IsEqualTo("Test Blog");
        await Assert.That(feed.Links.Any(l => l.RelationshipType == "self" && l.Uri.AbsoluteUri == "https://blog.example/feed.xml")).IsTrue();
        await Assert.That(item.Links.Single().Uri.AbsoluteUri).IsEqualTo("https://blog.example/posts/2026/09/22/post-1");
        await Assert.That(item.Id).IsEqualTo("https://blog.example/posts/2026/09/22/post-1");
        await Assert.That(item.Categories.Single().Name).IsEqualTo("C#");
        await Assert.That(html).Contains("href=\"https://blog.example/posts\"");
        await Assert.That(html).Contains("href=\"https://blog.example/posts/2026/09/22/post-1#fn:1\"");
        await Assert.That(html).DoesNotContain("<script");
    }

    /// <summary>RSS writes its Atom extensions with the conventional <c>atom</c> prefix, as the W3C validator recommends.</summary>
    [Test]
    public async Task Rss_UsesAtomPrefix()
    {
        var xml = Encoding.UTF8.GetString(Writer.Write(SyndicationFeedWriter.Format.Rss, Settings(FeedContentMode.FullContent), [Post],
            SiteUrl, new Uri(SiteUrl, "/feed.xml")));

        await Assert.That(xml).Contains("xmlns:atom=\"http://www.w3.org/2005/Atom\"");
        await Assert.That(xml).Contains("<atom:link rel=\"self\"");
        await Assert.That(xml).DoesNotContain("a10:");
    }

    /// <summary>With the summary setting, items carry only the summary.</summary>
    [Test]
    public async Task Rss_SummaryMode_HasOnlySummary()
    {
        var feed = Load(Writer.Write(SyndicationFeedWriter.Format.Rss, Settings(FeedContentMode.Summary), [Post], SiteUrl,
            new Uri(SiteUrl, "/feed.xml")));

        await Assert.That(feed.Items.Single().Summary.Text).IsEqualTo("Summary 1.");
    }

    /// <summary>Atom entries have HTML content plus the summary, and the feed has the author from the settings.</summary>
    [Test]
    public async Task Atom_HasContentSummaryAndAuthor()
    {
        var feed = Load(Writer.Write(SyndicationFeedWriter.Format.Atom, Settings(FeedContentMode.FullContent), [Post], SiteUrl,
            new Uri(SiteUrl, "/atom.xml")));

        var entry = feed.Items.Single();
        await Assert.That(feed.Authors.Single().Name).IsEqualTo("Jane Author");
        await Assert.That(feed.Id).IsEqualTo("https://blog.example/");
        await Assert.That(((TextSyndicationContent)entry.Content).Text).Contains("href=\"https://blog.example/posts\"");
        await Assert.That(entry.Summary.Text).IsEqualTo("Summary 1.");
        await Assert.That(entry.LastUpdatedTime).IsEqualTo(Post.Post.PublishedOn);
    }

    /// <summary>A tag feed is titled after the tag and links to the tag page; an empty feed is still valid.</summary>
    [Test]
    public async Task TagFeed_UsesTagTitleAndLink()
    {
        var tag = new PublicTag(3, "C#", "csharp", "All about C#.", 1);

        var feed = Load(Writer.Write(SyndicationFeedWriter.Format.Rss, Settings(FeedContentMode.FullContent), [], SiteUrl,
            new Uri(SiteUrl, "/tags/csharp/feed.xml"), tag));

        await Assert.That(feed.Title.Text).IsEqualTo("Test Blog: C#");
        await Assert.That(feed.Description.Text).IsEqualTo("All about C#.");
        await Assert.That(feed.Links.Any(l => l.Uri.AbsoluteUri == "https://blog.example/tags/csharp")).IsTrue();
        await Assert.That(feed.Items).IsEmpty();
    }

    private static SiteSettingsDto Settings(FeedContentMode mode)
    {
        return new SiteSettingsDto
        {
            SiteTitle = "Test Blog",
            Tagline = "Tests all the way down",
            AuthorName = "Jane Author",
            FeedContentMode = mode
        };
    }

    private static SyndicationFeed Load(byte[] xml)
    {
        using var reader = XmlReader.Create(new MemoryStream(xml));
        return SyndicationFeed.Load(reader);
    }
}