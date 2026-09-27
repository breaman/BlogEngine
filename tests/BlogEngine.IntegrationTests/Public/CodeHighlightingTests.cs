using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests that code highlighting loads only where it is needed (design 10.3, P12, Q7, T1.23): the render flags
/// posts with code blocks, only flagged posts carry the marker the page script looks for, and the highlighting
/// bundle is served as its own file.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class CodeHighlightingTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A post with a fenced code block is flagged, so public.js loads code-blocks.js for it.</summary>
    [Test]
    public async Task CodePost_IsFlaggedForHighlighting()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Code {PublicTestPosts.Token()}",
            ContentMarkdown = "Example:\n\n```csharp\nvar x = 1;\n```\n"
        }, PublicTestPosts.Noon(PublicTestPosts.NextYear(), 1, 1));
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains("<div class=\"post-content\" data-code-blocks=\"true\">");
        await Assert.That(html).Contains("<code class=\"language-csharp\">");
    }

    /// <summary>
    /// A text-only post isn't flagged and references no highlighting code; only the small loader, present on every
    /// page, is referenced.
    /// </summary>
    [Test]
    public async Task TextPost_LoadsNoHighlighting()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Text {PublicTestPosts.Token()}",
            ContentMarkdown = "Just words, and `inline code`, which needs no highlighting."
        }, PublicTestPosts.Noon(PublicTestPosts.NextYear(), 1, 1));
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains("<div class=\"post-content\">");
        await Assert.That(html).DoesNotContain("data-code-blocks");
        await Assert.That(html).DoesNotContain("<script src=\"js/code-blocks");
        await Assert.That(html).DoesNotContain("code-blocks.css");
        await Assert.That(html).Contains("<script src=\"js/public.");
    }

    /// <summary>The loader and the highlighting bundle with its theme are served as static files.</summary>
    [Test]
    [Arguments("/js/public.js", "code-blocks.js")]
    [Arguments("/js/code-blocks.js", "copy")]
    [Arguments("/js/code-blocks.css", ".hljs")]
    public async Task Scripts_AreServed(string path, string expectedContent)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(expectedContent);
    }
}