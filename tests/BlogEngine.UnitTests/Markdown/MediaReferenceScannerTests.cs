using BlogEngine.Shared.Markdown;

namespace BlogEngine.UnitTests.Markdown;

/// <summary>
/// Tests <see cref="MediaReferenceScanner"/>, which feeds <c>PostMedia</c> usage tracking (T1.2).
/// </summary>
public class MediaReferenceScannerTests
{
    /// <summary>Images, links, raw HTML and absolute URLs to the library all count.</summary>
    [Test]
    public async Task FindPublicIds_FindsEveryReferenceShape()
    {
        const string markdown = """
            ![Sunset](/media/aaaaaaaaaaa1/sunset.jpg "Caption"){.img-medium}
            [Download](/media/aaaaaaaaaaa2/report.png)
            <img src="/media/aaaaaaaaaaa3/raw.webp" alt="">
            ![Absolute](https://blog.example/media/aaaaaaaaaaa4/abs.jpg?v=3)
            """;

        var ids = MediaReferenceScanner.FindPublicIds(markdown);

        await Assert.That(ids).IsEquivalentTo(["aaaaaaaaaaa1", "aaaaaaaaaaa2", "aaaaaaaaaaa3", "aaaaaaaaaaa4"]);
    }

    /// <summary>Each item is reported once, in order of first use.</summary>
    [Test]
    public async Task FindPublicIds_DeduplicatesInOrder()
    {
        const string markdown = "![b](/media/bbbbbbbbbbbb/b.jpg) ![a](/media/aaaaaaaaaaaa/a.jpg) ![b again](/media/bbbbbbbbbbbb/b.jpg)";

        var ids = MediaReferenceScanner.FindPublicIds(markdown);

        await Assert.That(ids).IsEquivalentTo(["bbbbbbbbbbbb", "aaaaaaaaaaaa"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>Look-alike paths that aren't library URLs are ignored.</summary>
    [Test]
    [Arguments("![x](/social/media/aaaaaaaaaaaa/x.jpg)")]
    [Arguments("![x](/media/short/x.jpg)")]
    [Arguments("![x](/media/aaaaaaaaaaaaa/x.jpg)")]
    [Arguments("See /media/ for details")]
    [Arguments("")]
    public async Task FindPublicIds_IgnoresNonLibraryPaths(string markdown)
    {
        await Assert.That(MediaReferenceScanner.FindPublicIds(markdown)).IsEmpty();
    }
}