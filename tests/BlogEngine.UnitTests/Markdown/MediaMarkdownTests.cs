using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Markdown;

namespace BlogEngine.UnitTests.Markdown;

/// <summary>
/// Tests <see cref="MediaMarkdown"/> (design 9.5, T2.11): the media picker inserts standard Markdown that renders as
/// the intended figure.
/// </summary>
public class MediaMarkdownTests
{
    /// <summary>The picker's output for each option.</summary>
    [Test]
    [Arguments("Sunset", null, MediaImageSize.Full, false, "![Sunset](/media/ab12cd34ef56/sunset.jpg)")]
    [Arguments("Sunset", "At dusk", MediaImageSize.Full, false, "![Sunset](/media/ab12cd34ef56/sunset.jpg \"At dusk\")")]
    [Arguments("Sunset", "", MediaImageSize.Medium, false, "![Sunset](/media/ab12cd34ef56/sunset.jpg){.img-medium}")]
    [Arguments("Sunset", null, MediaImageSize.Small, true, "![Sunset](/media/ab12cd34ef56/sunset.jpg){.img-small .img-center}")]
    [Arguments("", null, MediaImageSize.Full, true, "![](/media/ab12cd34ef56/sunset.jpg){.img-center}")]
    public async Task Image_BuildsMarkdown(string alt, string? caption, MediaImageSize size, bool center, string expected)
    {
        await Assert.That(MediaMarkdown.Image("ab12cd34ef56", "sunset.jpg", alt, caption, size, center)).IsEqualTo(expected);
    }

    /// <summary>Brackets, quotes, backslashes and line breaks can't break out of the image syntax.</summary>
    [Test]
    public async Task Image_EscapesSpecialCharacters()
    {
        var markdown = MediaMarkdown.Image("ab12cd34ef56", "sunset.jpg", "A [bracketed]\nalt \\ text", "Say \"cheese\"");

        await Assert.That(markdown).IsEqualTo(@"![A \[bracketed\] alt \\ text](/media/ab12cd34ef56/sunset.jpg ""Say \""cheese\"""")");
    }

    /// <summary>What the picker inserts renders as the figure it promises.</summary>
    [Test]
    public async Task Image_RendersAsFigure()
    {
        var lookup = new MediaLookup([new MediaLookupItem(1, "ab12cd34ef56", "sunset.jpg", 640, 480, 2, "")]);
        var markdown = MediaMarkdown.Image("ab12cd34ef56", "sunset.jpg", "A [nice] \"sunset\"", "Say \"cheese\"", MediaImageSize.Medium, center: true);

        var html = BlogMarkdownPipeline.Default.RenderPost(markdown, lookup).Html;

        await Assert.That(html).IsEqualTo(
            "<figure class=\"media-figure img-medium img-center\"><img src=\"/media/ab12cd34ef56/sunset.jpg?v=2\" "
            + "alt=\"A [nice] &quot;sunset&quot;\" width=\"640\" height=\"480\" loading=\"lazy\" decoding=\"async\">"
            + "<figcaption>Say &quot;cheese&quot;</figcaption></figure>\n");
    }
}