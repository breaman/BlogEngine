using System.Text.RegularExpressions;

using BlogEngine.Shared.Markdown;

namespace BlogEngine.UnitTests.Markdown;

/// <summary>
/// Tests <see cref="BlogMarkdownPipeline.RenderPostPreview"/> (design 10.2, T1.11): the editor preview is the
/// published HTML plus source-line attributes for scroll sync, and nothing else.
/// </summary>
public partial class PostPreviewRenderingTests
{
    /// <summary>A post that exercises most block types and extensions.</summary>
    private const string RichPost = """
        # Heading

        A paragraph with **bold**, a [link](https://example.com) and `code`.

        - one
        - two
          - nested

        > A quote

        | A | B |
        |---|---|
        | 1 | 2 |

        ```csharp
        var x = 1;
        ```

        <div class="raw">Raw HTML</div>

        - [x] done

        Text with a footnote[^1].

        [^1]: The note.

        ![Alt](/media/abcdefabcdef/photo.jpg){.img-medium}
        """;

    /// <summary>Removing the source-line attributes gives exactly what the server stores on save.</summary>
    [Test]
    public async Task RenderPostPreview_WithoutSourceLines_MatchesPublishedHtml()
    {
        var preview = BlogMarkdownPipeline.Default.RenderPostPreview(RichPost);
        var published = BlogMarkdownPipeline.Default.RenderPost(RichPost);

        await Assert.That(SourceLineAttributeRegex().Replace(preview.Html, string.Empty)).IsEqualTo(published.Html);
        await Assert.That(preview.ContainsCodeBlocks).IsEqualTo(published.ContainsCodeBlocks);
    }

    /// <summary>Each top-level block records the 0-based line it starts on.</summary>
    [Test]
    public async Task RenderPostPreview_TagsTopLevelBlocksWithSourceLine()
    {
        var html = BlogMarkdownPipeline.Default.RenderPostPreview("# Title\n\nFirst\n\n\nSecond").Html;

        await Assert.That(html).Contains("<h1 id=\"title\" data-line=\"0\"><a href=\"#title\" class=\"heading-anchor\">Title</a></h1>");
        await Assert.That(html).Contains("<p data-line=\"2\">First</p>");
        await Assert.That(html).Contains("<p data-line=\"5\">Second</p>");
    }

    /// <summary>Only top-level blocks are tagged, so the preview's lookup is unambiguous.</summary>
    [Test]
    public async Task RenderPostPreview_DoesNotTagNestedBlocks()
    {
        var html = BlogMarkdownPipeline.Default.RenderPostPreview("- one\n- two").Html;

        await Assert.That(html).Contains("<ul data-line=\"0\">");
        await Assert.That(SourceLineAttributeRegex().Count(html)).IsEqualTo(1);
    }

    /// <summary>Raw HTML blocks are written verbatim, so they can't carry the attribute and are left alone.</summary>
    [Test]
    public async Task RenderPostPreview_LeavesRawHtmlBlocksUnchanged()
    {
        var html = BlogMarkdownPipeline.Default.RenderPostPreview("<section>Raw</section>").Html;

        await Assert.That(html).IsEqualTo("<section>Raw</section>\n");
    }

    [GeneratedRegex(" data-line=\"\\d+\"")]
    private static partial Regex SourceLineAttributeRegex();
}
