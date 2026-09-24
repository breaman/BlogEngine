using System.Text.Json;

using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>Tests <see cref="BlogPostingSchema"/>: the JSON-LD a post page gives search engines (P8, T4.7).</summary>
public class BlogPostingSchemaTests
{
    private static readonly Uri Url = new("https://blog.example/posts/2026/09/22/hello");
    private static readonly Uri Site = new("https://blog.example/");

    private static readonly SeoArticle Article = new("Hello, world", new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.FromHours(-5)),
        new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero), ["C#", "Blazor"]);

    /// <summary>A complete BlogPosting: headline, URL, UTC dates, description, image, author and keywords.</summary>
    [Test]
    public async Task ToJson_HasBlogPostingProperties()
    {
        using var json = JsonDocument.Parse(BlogPostingSchema.ToJson(new BlogPostingSchema.Data(
            Url, Article, "About hello.", "https://blog.example/media/a/b.png", "Ada Lovelace", "My blog", Site)));
        var root = json.RootElement;

        await Assert.That(root.GetProperty("@context").GetString()).IsEqualTo("https://schema.org");
        await Assert.That(root.GetProperty("@type").GetString()).IsEqualTo("BlogPosting");
        await Assert.That(root.GetProperty("headline").GetString()).IsEqualTo("Hello, world");
        await Assert.That(root.GetProperty("url").GetString()).IsEqualTo(Url.AbsoluteUri);
        await Assert.That(root.GetProperty("mainEntityOfPage").GetProperty("@id").GetString()).IsEqualTo(Url.AbsoluteUri);
        await Assert.That(root.GetProperty("datePublished").GetString()).IsEqualTo("2026-09-22T17:00:00Z");
        await Assert.That(root.GetProperty("dateModified").GetString()).IsEqualTo("2026-09-30T08:00:00Z");
        await Assert.That(root.GetProperty("description").GetString()).IsEqualTo("About hello.");
        await Assert.That(root.GetProperty("image")[0].GetString()).IsEqualTo("https://blog.example/media/a/b.png");
        await Assert.That(root.GetProperty("author").GetProperty("@type").GetString()).IsEqualTo("Person");
        await Assert.That(root.GetProperty("author").GetProperty("name").GetString()).IsEqualTo("Ada Lovelace");
        await Assert.That(root.GetProperty("isPartOf").GetProperty("name").GetString()).IsEqualTo("My blog");
        await Assert.That(root.GetProperty("keywords").GetString()).IsEqualTo("C#, Blazor");
    }

    /// <summary>Without an author name the blog is the author; missing optional values are left out.</summary>
    [Test]
    public async Task ToJson_NoAuthorOrImage_UsesSiteAndOmitsImage()
    {
        using var json = JsonDocument.Parse(BlogPostingSchema.ToJson(new BlogPostingSchema.Data(
            Url, Article with { Tags = [] }, null, null, " ", "My blog", Site)));
        var root = json.RootElement;

        await Assert.That(root.GetProperty("author").GetProperty("@type").GetString()).IsEqualTo("Organization");
        await Assert.That(root.GetProperty("author").GetProperty("name").GetString()).IsEqualTo("My blog");
        await Assert.That(root.TryGetProperty("image", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("description", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("keywords", out _)).IsFalse();
    }

    /// <summary>Markup in a title is escaped, so the JSON can't end its script element early.</summary>
    [Test]
    public async Task ToJson_EscapesHtmlSensitiveCharacters()
    {
        var json = BlogPostingSchema.ToJson(new BlogPostingSchema.Data(
            Url, Article with { Headline = "</script><b>Tom & \"Jerry\"</b>" }, null, null, "Zoë", "My blog", Site));

        await Assert.That(json).DoesNotContain("<");
        await Assert.That(json).DoesNotContain(">");
        await Assert.That(json).DoesNotContain("&");
        await Assert.That(json).Contains("Zoë");
        await Assert.That(JsonDocument.Parse(json).RootElement.GetProperty("headline").GetString()).IsEqualTo("</script><b>Tom & \"Jerry\"</b>");
    }

    /// <summary>A long title is shortened at a word boundary to the headline limit.</summary>
    [Test]
    public async Task ToJson_ShortensLongHeadline()
    {
        var title = string.Join(' ', Enumerable.Repeat("wordy", 40));

        using var json = JsonDocument.Parse(BlogPostingSchema.ToJson(new BlogPostingSchema.Data(
            Url, Article with { Headline = title }, null, null, null, "My blog", Site)));
        var headline = json.RootElement.GetProperty("headline").GetString()!;

        await Assert.That(headline.Length).IsLessThanOrEqualTo(BlogPostingSchema.MaxHeadlineLength);
        await Assert.That(headline).EndsWith("wordy…");
    }

    /// <summary>An update stamped before a re-dated publish time never makes the modified date precede publication.</summary>
    [Test]
    public async Task ToJson_ModifiedNeverBeforePublished()
    {
        using var json = JsonDocument.Parse(BlogPostingSchema.ToJson(new BlogPostingSchema.Data(
            Url, Article with { ModifiedOn = Article.PublishedOn.AddDays(-3) }, null, null, null, "My blog", Site)));

        await Assert.That(json.RootElement.GetProperty("dateModified").GetString()).IsEqualTo("2026-09-22T17:00:00Z");
    }
}
