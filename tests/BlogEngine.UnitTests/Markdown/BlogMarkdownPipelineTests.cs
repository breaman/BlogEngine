using BlogEngine.Shared.Markdown;

using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.UnitTests.Markdown;

/// <summary>
/// Snapshot tests for <see cref="BlogMarkdownPipeline"/>: one per post extension (design 10.1), plus proof
/// that the comment pipeline drops headings, images, tables and HTML (design 8.2).
/// </summary>
/// <remarks>
/// Snapshots live in <c>Markdown/Snapshots</c>. When output changes on purpose, review the
/// <c>.received.html</c> file and replace the matching <c>.verified.html</c>.
/// </remarks>
public class BlogMarkdownPipelineTests
{
    private static readonly BlogMarkdownPipeline Pipeline = new(new BlogMarkdownOptions
    {
        InternalHosts = ["blog.example.com"]
    });

    /// <summary>Pipe tables render as tables.</summary>
    [Test]
    public Task Post_PipeTables()
    {
        return VerifyPost("""
            | Language | Typing  |
            |----------|:-------:|
            | C#       | Static  |
            | Python   | Dynamic |
            """);
    }

    /// <summary>Grid tables render as tables.</summary>
    [Test]
    public Task Post_GridTables()
    {
        return VerifyPost("""
            +---------+---------+
            | Header  | Header  |
            +=========+=========+
            | Cell    | Cell    |
            +---------+---------+
            """);
    }

    /// <summary>Task list items render as disabled checkboxes.</summary>
    [Test]
    public Task Post_TaskLists()
    {
        return VerifyPost("""
            - [x] Write the post
            - [ ] Publish it
            """);
    }

    /// <summary>Footnote references link to a footnotes section.</summary>
    [Test]
    public Task Post_Footnotes()
    {
        return VerifyPost("""
            Markdig is fast.[^1]

            [^1]: See the benchmarks.
            """);
    }

    /// <summary>Headings get GitHub-style ids, de-duplicated with a suffix.</summary>
    [Test]
    public Task Post_AutoIdentifiers()
    {
        return VerifyPost("""
            # Getting Started with C#

            ## What's New?

            ## What's New?
            """);
    }

    /// <summary>Strikethrough and marked text work; a single tilde stays literal.</summary>
    [Test]
    public Task Post_EmphasisExtras()
    {
        return VerifyPost("~~old~~ ==highlighted== and ~approximately~ 5");
    }

    /// <summary>Bare URLs become links (and, being external, get the external-link treatment).</summary>
    [Test]
    public Task Post_AutoLinks()
    {
        return VerifyPost("Visit https://example.com/docs or www.example.org today.");
    }

    /// <summary>YouTube embeds use youtube-nocookie.com; Vimeo embeds as usual; other hosts stay images.</summary>
    [Test]
    public async Task Post_MediaLinks()
    {
        var result = Pipeline.RenderPost("""
            ![Talk](https://www.youtube.com/watch?v=mswPy5bt3TQ)

            ![Short link with start](https://youtu.be/mswPy5bt3TQ?t=100)

            ![Vimeo](https://vimeo.com/8607834)

            ![Unsupported host](https://ok.ru/video/26870090463)
            """);

        await Assert.That(result.Html).DoesNotContain("www.youtube.com/embed");
        await Assert.That(result.Html).Contains("https://www.youtube-nocookie.com/embed/mswPy5bt3TQ");
        await Assert.That(result.Html).DoesNotContain("ok.ru/videoembed");
        await VerifyHtml(result.Html);
    }

    /// <summary>Generic attributes add classes and ids, such as image size hints.</summary>
    [Test]
    public Task Post_GenericAttributes()
    {
        return VerifyPost("""
            ![Sunset](/media/ab12cd34ef56/sunset.jpg "Sunset over the lake"){.img-medium}

            ## Custom Anchor {#my-anchor}
            """);
    }

    /// <summary>Figure blocks render as figure elements with captions.</summary>
    [Test]
    public Task Post_Figures()
    {
        return VerifyPost("""
            ^^^
            ![Diagram](/media/ab12cd34ef56/diagram.png)
            ^^^ The architecture at a glance
            """);
    }

    /// <summary>Raw HTML passes through in posts, because the author is trusted.</summary>
    [Test]
    public Task Post_RawHtmlAllowed()
    {
        return VerifyPost("""
            <div class="callout">A <strong>trusted</strong> block</div>

            Inline <kbd>Ctrl</kbd>+<kbd>S</kbd> saves.
            """);
    }

    /// <summary>
    /// External links get <c>rel="noopener"</c>, a class and an icon; internal, relative and mailto links
    /// don't; an author-supplied <c>rel</c> is kept.
    /// </summary>
    [Test]
    public async Task Post_ExternalLinks()
    {
        var result = Pipeline.RenderPost("""
            - [External](https://example.com/page)
            - [Protocol-relative](//example.com/page)
            - [Own absolute URL](https://blog.example.com/posts)
            - [Relative](/posts/2026/09/22/hello)
            - [Anchor](#section)
            - [Email](mailto:someone@example.com)
            - [Author rel](https://example.com/sponsor){rel="sponsored"}
            """);

        await Assert.That(CountOccurrences(result.Html, ExternalLinkRewriter.IconHtml)).IsEqualTo(3);
        await VerifyHtml(result.Html);
    }

    /// <summary>Every inline records its exact source position, which editor scroll sync relies on.</summary>
    [Test]
    public async Task Post_PreciseSourceLocation()
    {
        const string markdown = "# Title\n\nSome text with **bold** here.";

        var document = Pipeline.ParsePost(markdown);
        var emphasis = document.Descendants<EmphasisInline>().Single();
        var heading = document.Descendants<HeadingBlock>().Single();

        await Assert.That(emphasis.Span.Start).IsEqualTo(markdown.IndexOf("**bold**", StringComparison.Ordinal));
        await Assert.That(emphasis.Span.Length).IsEqualTo("**bold**".Length);
        await Assert.That(heading.Line).IsEqualTo(0);
    }

    /// <summary>The code block flag is set for fenced and indented blocks only, not inline code.</summary>
    [Test]
    [Arguments("```csharp\nvar x = 1;\n```", true)]
    [Arguments("Intro:\n\n    var x = 1;", true)]
    [Arguments("Use `var` here.", false)]
    [Arguments("Plain text only.", false)]
    public async Task Post_FlagsCodeBlocks(string markdown, bool expected)
    {
        await Assert.That(Pipeline.RenderPost(markdown).ContainsCodeBlocks).IsEqualTo(expected);
    }

    /// <summary>The comment pipeline keeps paragraphs, emphasis, code, links, quotes and lists.</summary>
    [Test]
    public Task Comment_AllowedFeatures()
    {
        return VerifyComment("""
            Great post! I *really* liked the **second** part.

            > Quoting the author

            - First point with `inline code`
            - Second point, see [the docs](https://learn.microsoft.com)

            1. Numbered
            2. List

            ```
            var answer = 42;
            ```
            """);
    }

    /// <summary>ATX and setext headings render as plain paragraph text, and horizontal rules are dropped.</summary>
    [Test]
    public async Task Comment_DropsHeadings()
    {
        var result = Pipeline.RenderComment("""
            # Look at me

            Also me
            =======

            ---
            """);

        await Assert.That(result.Html).DoesNotContain("<h");
        await Assert.That(result.Html).DoesNotContain("<hr");
        await VerifyHtml(result.Html);
    }

    /// <summary>Images become plain links, so nothing is embedded.</summary>
    [Test]
    public async Task Comment_DropsImages()
    {
        var result = Pipeline.RenderComment("""
            ![tracking pixel](https://evil.example/pixel.gif)

            ![](https://evil.example/no-alt.png)

            ![video](https://www.youtube.com/watch?v=mswPy5bt3TQ)
            """);

        await Assert.That(result.Html).DoesNotContain("<img");
        await Assert.That(result.Html).DoesNotContain("<iframe");
        await VerifyHtml(result.Html);
    }

    /// <summary>Table syntax renders as plain text.</summary>
    [Test]
    public async Task Comment_DropsTables()
    {
        var result = Pipeline.RenderComment("""
            | a | b |
            |---|---|
            | c | d |
            """);

        await Assert.That(result.Html).DoesNotContain("<table");
        await VerifyHtml(result.Html);
    }

    /// <summary>Raw HTML, block and inline, is escaped rather than rendered.</summary>
    [Test]
    public async Task Comment_DropsHtml()
    {
        var result = Pipeline.RenderComment("""
            <script>alert('xss')</script>

            Inline <b onmouseover="alert(1)">bold</b> and <img src=x onerror=alert(1)>
            """);

        await Assert.That(result.Html).DoesNotContain("<script");
        await Assert.That(result.Html).DoesNotContain("<b ");
        await Assert.That(result.Html).DoesNotContain("<img");
        await Assert.That(result.Html).Contains("&lt;script&gt;");
        await VerifyHtml(result.Html);
    }

    private static SettingsTask VerifyPost(string markdown)
    {
        return VerifyHtml(Pipeline.RenderPost(markdown).Html);
    }

    private static SettingsTask VerifyComment(string markdown)
    {
        return VerifyHtml(Pipeline.RenderComment(markdown).Html);
    }

    private static SettingsTask VerifyHtml(string html)
    {
        return Verify(html, "html").UseDirectory("Snapshots");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}