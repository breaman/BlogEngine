using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests heading anchors and the table of contents on the post page (P13, T4.13): headings link to themselves, posts
/// with four or more sections list them above the content, and every in-page link includes the post's path, because the
/// site's <c>&lt;base href="/"&gt;</c> would otherwise send a bare <c>#fragment</c> to the home page.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class TableOfContentsTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A long post gets a table of contents whose links match the heading ids, h3 headings nested.</summary>
    [Test]
    public async Task LongPost_HasTableOfContents_LinkingToHeadings()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Long read {PublicTestPosts.Token()}",
            ContentMarkdown = """
                ## Setup

                ### Install the SDK

                ## Usage

                ## Pitfalls

                ## Wrapping up
                """
        });
        using var client = IdentityTestHelper.CreateClient(factory);
        var path = post.PublicPath!;

        var html = await PublicTestPosts.GetOkAsync(client, path);
        var toc = html[html.IndexOf("class=\"post-toc", StringComparison.Ordinal)..html.IndexOf("class=\"post-content\"", StringComparison.Ordinal)];

        await Assert.That(toc).Contains($"<a href=\"{path}#setup\">Setup</a>");
        await Assert.That(toc).Contains($"<ol><li><a href=\"{path}#install-the-sdk\">Install the SDK</a></li></ol>");
        await Assert.That(toc).Contains($"<a href=\"{path}#wrapping-up\">Wrapping up</a>");
        await Assert.That(html).Contains($"<h2 id=\"setup\"><a href=\"{path}#setup\" class=\"heading-anchor\">Setup</a></h2>");
        await Assert.That(html).Contains($"<h3 id=\"install-the-sdk\"><a href=\"{path}#install-the-sdk\" class=\"heading-anchor\">Install the SDK</a></h3>");
    }

    /// <summary>A post with fewer than four sections has no table of contents, but its headings still link to themselves.</summary>
    [Test]
    public async Task ShortPost_HasNoTableOfContents()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Short read {PublicTestPosts.Token()}",
            ContentMarkdown = "## One\n\n## Two\n\n## Three"
        });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).DoesNotContain("post-toc");
        await Assert.That(html).Contains($"<a href=\"{post.PublicPath}#two\" class=\"heading-anchor\">Two</a>");
    }

    /// <summary>Footnote links point at this post too, instead of the home page.</summary>
    [Test]
    public async Task FootnoteLinks_IncludePostPath()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Footnoted {PublicTestPosts.Token()}",
            ContentMarkdown = "A claim.[^1]\n\n[^1]: The source."
        });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains($"href=\"{post.PublicPath}#fn:1\"");
        await Assert.That(html).Contains($"href=\"{post.PublicPath}#fnref:1\"");
        await Assert.That(html).DoesNotContain("href=\"#fn");
    }
}