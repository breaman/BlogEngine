using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Markdown;

namespace BlogEngine.UnitTests.Markdown;

/// <summary>
/// Snapshot tests for <see cref="MediaLinkRewriter"/> through <see cref="BlogMarkdownPipeline"/> (design 9.4, 9.5,
/// T2.7): a library image becomes a sized, lazily loaded figure with a cache-busting version; a missing image is a
/// placeholder in the preview and left out of published HTML.
/// </summary>
/// <remarks>Snapshots live in <c>Markdown/Snapshots</c>, like the pipeline's.</remarks>
public class MediaLinkRewriterTests
{
    private const string SunsetId = "ab12cd34ef56";

    private static readonly BlogMarkdownPipeline Pipeline = new(new BlogMarkdownOptions { InternalHosts = ["blog.example.com"] });

    private const string LakeId = "lake00000001";

    private static readonly MediaLookup Library = new(
    [
        new MediaLookupItem(1, SunsetId, "sunset.jpg", 1280, 853, 3, "Library alt text"),
        new MediaLookupItem(2, LakeId, "lake.jpg", 2400, 1600, 2, "A lake", [320, 640, 960, 1280, 1920])
    ]);

    /// <summary>A standalone library image becomes a figure with its size, version and lazy loading.</summary>
    [Test]
    public Task Published_NormalImage()
    {
        return VerifyPublished($"![Sunset over the lake](/media/{SunsetId}/sunset.jpg)");
    }

    /// <summary>The Markdown title becomes the caption.</summary>
    [Test]
    public Task Published_CaptionedImage()
    {
        return VerifyPublished($"![Sunset over the lake](/media/{SunsetId}/sunset.jpg \"At dusk, from the pier\")");
    }

    /// <summary>Size and alignment hints from the media picker land on the figure.</summary>
    [Test]
    public Task Published_SizeHint()
    {
        return VerifyPublished($"![Sunset](/media/{SunsetId}/sunset.jpg \"At dusk\"){{.img-medium .img-center}}");
    }

    /// <summary>A deleted image leaves no trace in published HTML.</summary>
    [Test]
    public async Task Published_MissingImage_IsOmitted()
    {
        var html = Pipeline.RenderPost("Before.\n\n![Gone](/media/zzzzzzzzzzzz/gone.jpg \"Caption\")\n\nAfter.", Library).Html;

        await Assert.That(html).DoesNotContain("gone.jpg");
        await Assert.That(html).DoesNotContain("<figure");
        await VerifyHtml(html);
    }

    /// <summary>The editor preview shows a visible placeholder for a deleted image, with its source line.</summary>
    [Test]
    public Task Preview_MissingImage_ShowsPlaceholder()
    {
        return VerifyHtml(Pipeline.RenderPostPreview("Intro.\n\n![Gone](/media/zzzzzzzzzzzz/gone.jpg)", Library).Html);
    }

    /// <summary>The preview renders the same figure as published HTML, plus the scroll-sync line.</summary>
    [Test]
    public async Task Preview_MatchesPublishedExceptSourceLine()
    {
        var markdown = $"# Title\n\n![Sunset](/media/{SunsetId}/sunset.jpg \"At dusk\"){{.img-small}}";

        var published = Pipeline.RenderPost(markdown, Library).Html;
        var preview = Pipeline.RenderPostPreview(markdown, Library).Html;

        await Assert.That(preview.Replace(" data-line=\"0\"", "").Replace(" data-line=\"2\"", "")).IsEqualTo(published);
        await Assert.That(preview).Contains("<figure class=\"media-figure img-small\" data-line=\"2\">");
    }

    /// <summary>An image inside a sentence stays inline and only gains its size and version.</summary>
    [Test]
    public Task Published_InlineImage()
    {
        return VerifyPublished($"Look at this ![tiny sunset](/media/{SunsetId}/sunset.jpg){{.img-small}} in the middle of a sentence.");
    }

    /// <summary>Without alt text in the Markdown, the library's alt text is used.</summary>
    [Test]
    public async Task Published_EmptyAlt_UsesLibraryAltText()
    {
        var html = Pipeline.RenderPost($"![](/media/{SunsetId}/sunset.jpg)", Library).Html;

        await Assert.That(html).Contains("alt=\"Library alt text\"");
    }

    /// <summary>Absolute URLs on the blog's own host are library images too; other hosts and paths are untouched.</summary>
    [Test]
    public async Task OnlyLibraryUrlsAreRewritten()
    {
        var html = Pipeline.RenderPost(
            $"![a](https://blog.example.com/media/{SunsetId}/sunset.jpg)\n\n"
            + $"![b](https://elsewhere.example.org/media/{SunsetId}/sunset.jpg)\n\n"
            + "![c](/images/local.png)\n\n"
            + $"![d](/social/media/{SunsetId}/x.jpg)",
            Library).Html;

        await Assert.That(html).Contains($"src=\"/media/{SunsetId}/sunset.jpg?v=3\"");
        await Assert.That(html).Contains($"src=\"https://elsewhere.example.org/media/{SunsetId}/sunset.jpg\"");
        await Assert.That(html).Contains("src=\"/images/local.png\"");
        await Assert.That(html).Contains($"src=\"/social/media/{SunsetId}/x.jpg\"");
    }

    /// <summary>Alt text and captions are HTML-encoded.</summary>
    [Test]
    public async Task EncodesAltAndCaption()
    {
        var html = Pipeline.RenderPost($"![A \"quoted\" <b>alt</b>](/media/{SunsetId}/sunset.jpg \"Fish & <chips>\")", Library).Html;

        await Assert.That(html).Contains("alt=\"A &quot;quoted&quot; alt\"");
        await Assert.That(html).Contains("<figcaption>Fish &amp; &lt;chips&gt;</figcaption>");
    }

    /// <summary>Without a lookup (as before Phase 2) library images render as plain images.</summary>
    [Test]
    public async Task WithoutLookup_LeavesImagesAlone()
    {
        var html = Pipeline.RenderPost($"![Sunset](/media/{SunsetId}/sunset.jpg)").Html;

        await Assert.That(html).IsEqualTo($"<p><img src=\"/media/{SunsetId}/sunset.jpg\" alt=\"Sunset\" /></p>\n");
    }

    /// <summary>Library URL recognition, including query strings and escaped names.</summary>
    [Test]
    [Arguments("/media/ab12cd34ef56/sunset.jpg", "ab12cd34ef56", "sunset.jpg")]
    [Arguments("/media/ab12cd34ef56/sunset.jpg?v=2", "ab12cd34ef56", "sunset.jpg")]
    [Arguments("/media/ab12cd34ef56/my%20photo.png", "ab12cd34ef56", "my photo.png")]
    [Arguments("https://blog.example.com/media/ab12cd34ef56/a.gif#top", "ab12cd34ef56", "a.gif")]
    public async Task TryParseLibraryUrl_Recognizes(string url, string publicId, string fileName)
    {
        var rewriter = new MediaLinkRewriter(["blog.example.com"]);

        await Assert.That(rewriter.TryParseLibraryUrl(url)).IsEqualTo((publicId, fileName));
    }

    /// <summary>Anything else is not a library URL.</summary>
    [Test]
    [Arguments("/media/short/sunset.jpg")]
    [Arguments("/media/ab12cd34ef56/")]
    [Arguments("/media/ab12cd34ef56/a/b.jpg")]
    [Arguments("media/ab12cd34ef56/sunset.jpg")]
    [Arguments("//evil.example/media/ab12cd34ef56/sunset.jpg")]
    [Arguments("javascript:alert(1)")]
    [Arguments("")]
    public async Task TryParseLibraryUrl_RejectsOthers(string url)
    {
        await Assert.That(new MediaLinkRewriter(["blog.example.com"]).TryParseLibraryUrl(url)).IsNull();
    }

    /// <summary>
    /// An image with renditions becomes a picture: WebP and own-format srcsets with the column's sizes, and a 1280 px
    /// fallback, keeping its natural size for the layout (T4.19).
    /// </summary>
    [Test]
    public Task Published_ImageWithRenditions()
    {
        return VerifyPublished($"![A lake](/media/{LakeId}/lake.jpg \"Morning\")");
    }

    /// <summary>The size hint decides <c>sizes</c>, so a small image asks for a small rendition.</summary>
    [Test]
    public async Task Published_ImageWithRenditions_SizesFollowHints()
    {
        var small = Pipeline.RenderPost($"![A lake](/media/{LakeId}/lake.jpg){{.img-small}}", Library).Html;
        var inline = Pipeline.RenderPost($"Tiny ![lake](/media/{LakeId}/lake.jpg){{.img-medium}} inline.", Library).Html;

        await Assert.That(small).Contains($"sizes=\"{MediaLinkRewriter.SizesFor(["img-small"])}\"");
        await Assert.That(inline).Contains("<picture><source type=\"image/webp\"");
        await Assert.That(inline).Contains($"sizes=\"{MediaLinkRewriter.SizesFor(["img-medium"])}\"");
        await Assert.That(inline).Contains("class=\"img-medium\"");
    }

    /// <summary>Without a rendition at or under 1280 px, the narrowest one is the fallback.</summary>
    [Test]
    public async Task Published_ImageWithRenditions_FallbackIsWidestUpTo1280()
    {
        var lookup = new MediaLookup([new MediaLookupItem(3, LakeId, "lake.jpg", 1000, 500, 1, "", [320, 640, 960, 1000])]);

        var html = Pipeline.RenderPost($"![x](/media/{LakeId}/lake.jpg)", lookup).Html;

        await Assert.That(html).Contains($"src=\"/media/{LakeId}/lake.jpg?w=1000&amp;v=1\"");
        await Assert.That(html).Contains("1000w\"");
    }

    /// <summary>The preview renders renditions the same way as published HTML.</summary>
    [Test]
    public async Task Preview_WithRenditions_MatchesPublished()
    {
        var markdown = $"![A lake](/media/{LakeId}/lake.jpg)";

        var published = Pipeline.RenderPost(markdown, Library).Html;
        var preview = Pipeline.RenderPostPreview(markdown, Library).Html;

        await Assert.That(preview.Replace(" data-line=\"0\"", "")).IsEqualTo(published);
    }

    private static SettingsTask VerifyPublished(string markdown)
    {
        return VerifyHtml(Pipeline.RenderPost(markdown, Library).Html);
    }

    private static SettingsTask VerifyHtml(string html)
    {
        return Verify(html, "html").UseDirectory("Snapshots");
    }
}
