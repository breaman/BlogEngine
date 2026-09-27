using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="FragmentLinks"/>: in-page links get the page's path, because the site's <c>&lt;base href="/"&gt;</c>
/// would send a bare <c>#fragment</c> to the home page (T4.13).
/// </summary>
public class FragmentLinksTests
{
    /// <summary>Heading anchors and footnote links point at the page.</summary>
    [Test]
    public async Task Resolve_PrefixesFragmentLinks()
    {
        var html = FragmentLinks.Resolve(
            "<h2 id=\"a\"><a href=\"#a\" class=\"heading-anchor\">A</a></h2><p>Text<sup><a href=\"#fn:1\">1</a></sup></p>",
            "/posts/2026/09/22/hello");

        await Assert.That(html).IsEqualTo(
            "<h2 id=\"a\"><a href=\"/posts/2026/09/22/hello#a\" class=\"heading-anchor\">A</a></h2><p>Text<sup><a href=\"/posts/2026/09/22/hello#fn:1\">1</a></sup></p>");
    }

    /// <summary>Other links, and <c>href="#"</c> shown as text in a code sample, are left alone.</summary>
    [Test]
    public async Task Resolve_LeavesOtherLinksAndText()
    {
        const string input = "<p><a href=\"/about\">About</a> <a href=\"https://example.com/#top\">x</a></p><pre><code>&lt;a href=\"#x\"&gt;</code></pre><a href=\"#y\">y</a>";

        var html = FragmentLinks.Resolve(input, "/about");

        await Assert.That(html).Contains("<a href=\"/about\">About</a>");
        await Assert.That(html).Contains("<a href=\"https://example.com/#top\">x</a>");
        await Assert.That(html).Contains("&lt;a href=\"#x\"&gt;");
        await Assert.That(html).Contains("<a href=\"/about#y\">y</a>");
    }

    /// <summary>HTML without fragment links comes back as it was, without being reparsed.</summary>
    [Test]
    public async Task Resolve_NoFragments_ReturnsInputUnchanged()
    {
        const string input = "<p>Plain <b>text</b></p>";

        await Assert.That(FragmentLinks.Resolve(input, "/about")).IsSameReferenceAs(input);
    }
}