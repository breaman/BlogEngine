using BlogEngine.Server.Services;
using BlogEngine.Shared.Markdown;

namespace BlogEngine.UnitTests.Server;

/// <summary>
/// Tests <see cref="PostHtmlSanitizer"/>: the permissive post allowlist keeps everything the Markdown
/// pipeline produces and removes script and foreign iframes (design 10.1, 12.2).
/// </summary>
public class PostHtmlSanitizerTests
{
    private static readonly PostHtmlSanitizer Sanitizer = new();

    /// <summary>Script, event handlers and <c>javascript:</c> URLs are removed.</summary>
    [Test]
    public async Task Sanitize_RemovesScript()
    {
        var html = Sanitizer.Sanitize("""
            <p onclick="alert(1)">Hi</p><script>alert(2)</script><a href="javascript:alert(3)">x</a>
            """);

        await Assert.That(html).DoesNotContain("script");
        await Assert.That(html).DoesNotContain("onclick");
        await Assert.That(html).DoesNotContain("javascript:");
        await Assert.That(html).Contains("<p>Hi</p>");
    }

    /// <summary>YouTube (no-cookie) and Vimeo embeds survive.</summary>
    [Test]
    [Arguments("https://www.youtube-nocookie.com/embed/mswPy5bt3TQ")]
    [Arguments("https://player.vimeo.com/video/8607834")]
    public async Task Sanitize_KeepsAllowedIframes(string src)
    {
        var html = Sanitizer.Sanitize($"""<iframe src="{src}" class="youtube" width="500" height="281" frameborder="0" allowfullscreen=""></iframe>""");

        await Assert.That(html).Contains($"src=\"{src}\"");
        await Assert.That(html).Contains("allowfullscreen");
    }

    /// <summary>Iframes from any other host, or over plain http, are removed entirely.</summary>
    [Test]
    [Arguments("https://evil.example/embed")]
    [Arguments("http://www.youtube-nocookie.com/embed/x")]
    [Arguments("https://www.youtube-nocookie.com.evil.example/embed/x")]
    [Arguments("")]
    public async Task Sanitize_RemovesForeignIframes(string src)
    {
        var html = Sanitizer.Sanitize($"""<p>Before</p><iframe src="{src}"></iframe>""");

        await Assert.That(html).DoesNotContain("iframe");
        await Assert.That(html).Contains("<p>Before</p>");
    }

    /// <summary>Forms can't be stored in posts, so a compromised account can't plant a phishing form.</summary>
    [Test]
    public async Task Sanitize_RemovesForms()
    {
        var html = Sanitizer.Sanitize("""<form action="https://evil.example"><button>Go</button></form>""");

        await Assert.That(html).DoesNotContain("<form");
        await Assert.That(html).DoesNotContain("<button");
        await Assert.That(html).DoesNotContain("evil.example");
    }

    /// <summary>
    /// Everything the post pipeline renders comes through unchanged in meaning: heading ids, footnote
    /// anchors, task-list checkboxes, figures, class hints, the external-link icon and mailto links.
    /// </summary>
    [Test]
    public async Task Sanitize_KeepsPipelineOutput()
    {
        const string markdown = """
            ## Heading

            Text with a footnote.[^1] Email [me](mailto:me@example.com).

            - [x] Done

            ^^^
            ![Diagram](/media/ab12cd34ef56/diagram.png){.img-medium}
            ^^^ Caption

            [External](https://example.com)

            [^1]: The note.
            """;
        var rendered = BlogMarkdownPipeline.Default.RenderPost(markdown).Html;

        var html = Sanitizer.Sanitize(rendered);

        await Assert.That(html).Contains("<h2 id=\"heading\">");
        await Assert.That(html).Contains("id=\"fnref:1\"");
        await Assert.That(html).Contains("class=\"footnote-ref\"");
        await Assert.That(html).Contains("type=\"checkbox\"");
        await Assert.That(html).Contains("<figure>");
        await Assert.That(html).Contains("<figcaption>");
        await Assert.That(html).Contains("class=\"img-medium\"");
        await Assert.That(html).Contains("bi-box-arrow-up-right");
        await Assert.That(html).Contains("href=\"mailto:me@example.com\"");
        await Assert.That(html).Contains("rel=\"noopener\"");
    }

    /// <summary>Null or empty input gives an empty string.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Sanitize_Empty_ReturnsEmpty(string? html)
    {
        await Assert.That(Sanitizer.Sanitize(html)).IsEmpty();
    }

    /// <summary>
    /// With a base URL (feeds), relative links become absolute: root-relative against the site, fragments against
    /// the post itself, so footnotes still work in a feed reader.
    /// </summary>
    [Test]
    public async Task Sanitize_WithBaseUrl_MakesUrlsAbsolute()
    {
        var html = Sanitizer.Sanitize("""<a href="/tags/csharp">C#</a><a href="#fn:1">1</a><img src="/media/abc/cat.jpg" alt="">""",
            new Uri("https://blog.example/posts/2026/09/22/hello"));

        await Assert.That(html).Contains("href=\"https://blog.example/tags/csharp\"");
        await Assert.That(html).Contains("href=\"https://blog.example/posts/2026/09/22/hello#fn:1\"");
        await Assert.That(html).Contains("src=\"https://blog.example/media/abc/cat.jpg\"");
    }
}
