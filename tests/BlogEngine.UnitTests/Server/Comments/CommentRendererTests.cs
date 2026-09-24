using BlogEngine.Server.Services.Comments;

namespace BlogEngine.UnitTests.Server.Comments;

/// <summary>
/// Tests <see cref="CommentRenderer"/> and <see cref="CommentHtmlSanitizer"/> (design 8.2, T3.2): the restricted
/// pipeline plus the sanitizer remove script, images, headings and raw HTML, and every link is nofollow.
/// </summary>
public class CommentRendererTests
{
    private static readonly CommentHtmlSanitizer Sanitizer = new();
    private static readonly CommentRenderer Renderer = new(Sanitizer);

    /// <summary>Raw HTML in the Markdown is escaped, so script never runs.</summary>
    [Test]
    public async Task RawHtml_IsEscaped()
    {
        var html = Renderer.Render("Hi <script>alert(1)</script> <b onclick=\"x()\">bold</b>");

        await Assert.That(html).DoesNotContain("<script");
        await Assert.That(html).DoesNotContain("<b ");
        await Assert.That(html).Contains("&lt;script&gt;");
    }

    /// <summary>Headings stay plain paragraph text, in both ATX and setext forms.</summary>
    [Test]
    public async Task Headings_AreNotRendered()
    {
        var html = Renderer.Render("# Big\n\nTitle\n=====");

        await Assert.That(html).DoesNotContain("<h1");
        await Assert.That(html).Contains("# Big");
    }

    /// <summary>Images become links, so nothing is embedded.</summary>
    [Test]
    public async Task Images_AreNotEmbedded()
    {
        var html = Renderer.Render("![tracker](https://evil.example/pixel.png)");

        await Assert.That(html).DoesNotContain("<img");
        await Assert.That(html).Contains("href=\"https://evil.example/pixel.png\"");
    }

    /// <summary>Tables are not part of the comment dialect.</summary>
    [Test]
    public async Task Tables_AreNotRendered()
    {
        var html = Renderer.Render("| a | b |\n|---|---|\n| 1 | 2 |");

        await Assert.That(html).DoesNotContain("<table");
    }

    /// <summary>Every link, internal or external, gets <c>rel="nofollow ugc noopener"</c>; <c>javascript:</c> links are dropped.</summary>
    [Test]
    public async Task Links_AreNofollowUgc()
    {
        var html = Renderer.Render("[site](https://ada.example) and [home](/about) and [bad](javascript:alert(1))");

        await Assert.That(html).Contains("<a href=\"https://ada.example\" class=\"external-link\" rel=\"nofollow ugc noopener\">");
        await Assert.That(html).Contains("<a href=\"/about\" rel=\"nofollow ugc noopener\">");
        await Assert.That(html).DoesNotContain("javascript:");
        await Assert.That(html).DoesNotContain("dofollow");
    }

    /// <summary>The allowed formatting survives: emphasis, code, code blocks, quotes and lists.</summary>
    [Test]
    public async Task AllowedFormatting_Survives()
    {
        var html = Renderer.Render("*em* **strong** `code`\n\n```csharp\nvar x = 1;\n```\n\n> quote\n\n- item");

        await Assert.That(html).Contains("<em>em</em>");
        await Assert.That(html).Contains("<strong>strong</strong>");
        await Assert.That(html).Contains("<code>code</code>");
        await Assert.That(html).Contains("<pre><code>var x = 1;");
        await Assert.That(html).Contains("<blockquote>");
        await Assert.That(html).Contains("<li>item</li>");
    }

    /// <summary>The sanitizer on its own drops elements the pipeline could never produce (with their content) and styles.</summary>
    [Test]
    public async Task Sanitizer_StripsDisallowedMarkup()
    {
        var html = Sanitizer.Sanitize(
            "<h2>x</h2><img src=\"a.png\"><iframe src=\"https://evil.example\"></iframe><p style=\"color:red\" class=\"evil\">ok</p><a href=\"https://a.example\" rel=\"dofollow\" target=\"_blank\">a</a>");

        await Assert.That(html).IsEqualTo("<p>ok</p><a href=\"https://a.example\" rel=\"nofollow ugc noopener\">a</a>");
    }
}
