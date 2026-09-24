using BlogEngine.Shared.Markdown;

namespace BlogEngine.UnitTests.Markdown;

/// <summary>Tests <see cref="HeadingAnchorExtension"/>: headings link to themselves (P13, T4.13).</summary>
public class HeadingAnchorExtensionTests
{
    /// <summary>The heading's text, formatting included, becomes a link to its id.</summary>
    [Test]
    public async Task Heading_WrapsTextInSelfLink()
    {
        var html = BlogMarkdownPipeline.Default.RenderPost("## Using `HttpClient` *well*").Html;

        await Assert.That(html).IsEqualTo(
            "<h2 id=\"using-httpclient-well\"><a href=\"#using-httpclient-well\" class=\"heading-anchor\">Using <code>HttpClient</code> <em>well</em></a></h2>\n");
    }

    /// <summary>An explicit id from generic attributes is the link target.</summary>
    [Test]
    public async Task ExplicitId_IsLinkTarget()
    {
        var html = BlogMarkdownPipeline.Default.RenderPost("## Setting things up {#setup}").Html;

        await Assert.That(html).Contains("<h2 id=\"setup\"><a href=\"#setup\" class=\"heading-anchor\">Setting things up</a></h2>");
    }

    /// <summary>Headings that already have a link, of any kind, are left alone: links can't be nested.</summary>
    [Test]
    [Arguments("## See [the docs](https://example.com)")]
    [Arguments("## Mail <https://example.com>")]
    [Arguments("## Raw <a href=\"/x\">link</a>")]
    public async Task HeadingWithLink_IsNotWrapped(string markdown)
    {
        var html = BlogMarkdownPipeline.Default.RenderPost(markdown).Html;

        await Assert.That(html).DoesNotContain(HeadingAnchorExtension.AnchorClass);
    }

    /// <summary>Other raw inline HTML, such as an abbreviation, doesn't count as a link.</summary>
    [Test]
    public async Task HeadingWithOtherHtml_IsWrapped()
    {
        var html = BlogMarkdownPipeline.Default.RenderPost("## The <abbr title=\"Document Object Model\">DOM</abbr>").Html;

        await Assert.That(html).Contains("class=\"heading-anchor\">The <abbr");
    }

    /// <summary>The preview shows the same anchors, so it matches the published page.</summary>
    [Test]
    public async Task Preview_HasSameAnchors()
    {
        var published = BlogMarkdownPipeline.Default.RenderPost("## Same").Html;
        var preview = BlogMarkdownPipeline.Default.RenderPostPreview("## Same").Html;

        await Assert.That(preview.Replace(" data-line=\"0\"", string.Empty, StringComparison.Ordinal)).IsEqualTo(published);
    }
}
