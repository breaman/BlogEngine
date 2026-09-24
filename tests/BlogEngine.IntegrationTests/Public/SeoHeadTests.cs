using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the full SEO head (design 14.2, 16, P8, T4.7): Open Graph and Twitter tags on every public page, the
/// <c>article:*</c> tags and JSON-LD <c>BlogPosting</c> on posts, <c>noindex</c> on search results, and the default
/// social image from the settings.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public partial class SeoHeadTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A post is described as an article: Open Graph, Twitter card and a BlogPosting that search engines can read.</summary>
    [Test]
    public async Task PostPage_HasOpenGraphTwitterAndBlogPosting()
    {
        var token = PublicTestPosts.Token();
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Structured {token}",
            Summary = $"Summary {token}",
            ContentMarkdown = "Body.",
            Tags = [$"Seo-{token}", "C#"]
        }, PublicTestPosts.Noon(PublicTestPosts.NextYear(), 4, 2));
        using var client = IdentityTestHelper.CreateClient(factory);
        var url = $"http://localhost{post.PublicPath}";

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        using var jsonLd = JsonDocument.Parse(WebUtility.HtmlDecode(JsonLd().Match(html).Groups[1].Value));
        var posting = jsonLd.RootElement;

        await Assert.That(html).Contains("<meta property=\"og:type\" content=\"article\"");
        await Assert.That(html).Contains($"<meta property=\"og:title\" content=\"Structured {token}\"");
        await Assert.That(html).Contains($"<meta property=\"og:description\" content=\"Summary {token}\"");
        await Assert.That(html).Contains($"<meta property=\"og:url\" content=\"{url}\"");
        await Assert.That(html).Contains("<meta property=\"og:site_name\"");
        await Assert.That(html).Contains($"<meta property=\"article:published_time\" content=\"{post.PublishedOn!.Value.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}\"");
        await Assert.That(html).Contains($"<meta property=\"article:tag\" content=\"Seo-{token}\"");
        // The card size depends on the default social image setting, which another test changes.
        await Assert.That(html).Contains("<meta name=\"twitter:card\" content=\"summary");
        await Assert.That(html).Contains($"<meta name=\"twitter:title\" content=\"Structured {token}\"");
        await Assert.That(posting.GetProperty("@type").GetString()).IsEqualTo("BlogPosting");
        await Assert.That(posting.GetProperty("headline").GetString()).IsEqualTo($"Structured {token}");
        await Assert.That(posting.GetProperty("url").GetString()).IsEqualTo(url);
        await Assert.That(posting.GetProperty("mainEntityOfPage").GetProperty("@id").GetString()).IsEqualTo(url);
        await Assert.That(posting.GetProperty("datePublished").GetString()).IsEqualTo($"{post.PublishedOn!.Value.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}");
        await Assert.That(posting.TryGetProperty("dateModified", out _)).IsTrue();
        await Assert.That(posting.TryGetProperty("author", out _)).IsTrue();
        await Assert.That(posting.GetProperty("keywords").GetString()).Contains($"Seo-{token}");
    }

    /// <summary>A list page is a website, with no article tags or JSON-LD.</summary>
    [Test]
    public async Task ListPage_IsWebsite_WithoutBlogPosting()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, "/tags");

        await Assert.That(html).Contains("<meta property=\"og:type\" content=\"website\"");
        await Assert.That(html).Contains("<meta property=\"og:url\" content=\"http://localhost/tags\"");
        await Assert.That(html).DoesNotContain("article:published_time");
        await Assert.That(JsonLd().IsMatch(html)).IsFalse();
        await Assert.That(html).DoesNotContain("BlogPosting");
    }

    /// <summary>A title with markup can't break out of the JSON-LD script element.</summary>
    [Test]
    public async Task JsonLd_EscapesScriptEndTag()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Evil </script><script>alert(1)</script> {PublicTestPosts.Token()}" });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).DoesNotContain("<script>alert(1)</script>");
        await Assert.That(JsonLd().Match(html).Groups[1].Value).Contains("\\u003C/script\\u003E");
    }

    /// <summary>Search results ask not to be indexed.</summary>
    [Test]
    public async Task SearchPage_IsNoIndex()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, "10.20.0.1");

        var html = await PublicTestPosts.GetOkAsync(client, "/search?q=anything");

        await Assert.That(html).Contains("<meta name=\"robots\" content=\"noindex, follow\"");
    }

    /// <summary>
    /// With a default social image in the settings, pages without their own image use it for Open Graph and a large
    /// Twitter card. Holds the settings lock and restores the settings.
    /// </summary>
    [Test]
    [NotInParallel(TestConstraints.SiteSettings)]
    public async Task DefaultSocialImage_UsedWhenPageHasNone()
    {
        var image = await MediaTestFiles.AddAsync(factory, $"default-social-{PublicTestPosts.Token()}.png", MediaTestFiles.Png(64, 32));
        var original = await GetSettingsAsync();
        using var client = IdentityTestHelper.CreateClient(factory);

        try
        {
            var changed = await GetSettingsAsync();
            changed.DefaultSocialImageMediaId = image.Id;
            await SaveSettingsAsync(changed);

            var html = await PublicTestPosts.GetOkAsync(client, "/tags");

            await Assert.That(html).Contains($"<meta property=\"og:image\" content=\"http://localhost{WebUtility.HtmlEncode(image.Url)}\"");
            await Assert.That(html).Contains("<meta property=\"og:image:width\" content=\"64\"");
            await Assert.That(html).Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"");
        }
        finally
        {
            await SaveSettingsAsync(original);
        }
    }

    private async Task<SiteSettingsDto> GetSettingsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync();
    }

    private async Task SaveSettingsAsync(SiteSettingsDto settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SaveAsync(settings);
    }

    // Razor encodes the "+" of the media type as &#x2B;, which HTML parsers read back as "+".
    [GeneratedRegex("<script type=\"application/ld(?:\\+|&#x2B;)json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLd();
}
