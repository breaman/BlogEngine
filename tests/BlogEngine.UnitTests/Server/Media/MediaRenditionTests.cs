using BlogEngine.Server.Endpoints;
using BlogEngine.Server.Services.Media;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BlogEngine.UnitTests.Server.Media;

/// <summary>
/// Tests the responsive renditions (design 9.4, M6, T4.19): which widths and formats an image gets
/// (<see cref="MediaRenditionWriter"/>), the copies <see cref="MediaProcessor.CreateRenditionsAsync"/> makes, when stored
/// renditions are out of date, and which one <c>?w=</c>/<c>?f=</c> serves (<see cref="MediaEndpoints.ChooseRendition"/>).
/// </summary>
public class MediaRenditionTests
{
    private static readonly int[] DefaultWidths = [320, 640, 960, 1280, 1920];
    private static readonly MediaProcessor Processor = new();

    /// <summary>Configured widths narrower than the image, plus the image's own width while it fits under the largest.</summary>
    [Test]
    [Arguments(1000, new[] { 320, 640, 960, 1000 })]
    [Arguments(4000, new[] { 320, 640, 960, 1280, 1920 })]
    [Arguments(1920, new[] { 320, 640, 960, 1280, 1920 })]
    [Arguments(640, new[] { 320, 640 })]
    [Arguments(200, new[] { 200 })]
    public async Task PlanWidths_SkipsWidthsWiderThanTheImage(int imageWidth, int[] expected)
    {
        await Assert.That(MediaRenditionWriter.PlanWidths(imageWidth, DefaultWidths)).IsEquivalentTo(expected);
    }

    /// <summary>Unsorted or duplicated settings still give ascending widths; no widths or no image give none.</summary>
    [Test]
    public async Task PlanWidths_NormalizesSettings()
    {
        await Assert.That(MediaRenditionWriter.PlanWidths(800, [640, 320, 640, 0])).IsEquivalentTo([320, 640]);
        await Assert.That(MediaRenditionWriter.PlanWidths(800, [])).IsEmpty();
        await Assert.That(MediaRenditionWriter.PlanWidths(0, DefaultWidths)).IsEmpty();
    }

    /// <summary>WebP for everything, plus the image's own format; GIFs keep their animation, so they get none.</summary>
    [Test]
    [Arguments("image/jpeg", new[] { "webp", "jpg" })]
    [Arguments("image/png", new[] { "webp", "png" })]
    [Arguments("image/webp", new[] { "webp" })]
    [Arguments("image/gif", new string[0])]
    public async Task PlanFormats_ByContentType(string contentType, string[] expected)
    {
        await Assert.That(MediaRenditionWriter.PlanFormats(contentType)).IsEquivalentTo(expected);
    }

    /// <summary>A JPEG gets a WebP and a JPEG copy at each width, with the aspect ratio kept.</summary>
    [Test]
    public async Task CreateRenditions_Jpeg_MakesWebpAndJpegAtEachWidth()
    {
        var source = TestImages.SplitImage(800, 400, JpegFormat.Instance);

        var renditions = await Processor.CreateRenditionsAsync(source, [320, 640], CancellationToken.None);

        await Assert.That(renditions.Select(r => (r.Width, r.Format))).IsEquivalentTo([(320, "webp"), (320, "jpg"), (640, "webp"), (640, "jpg")]);
        foreach (var rendition in renditions)
        {
            var info = Image.Identify(rendition.Content);
            await Assert.That((info.Width, info.Height)).IsEqualTo((rendition.Width, rendition.Width / 2));
            await Assert.That(info.Metadata.DecodedImageFormat).IsEqualTo(rendition.Format == "webp" ? WebpFormat.Instance : JpegFormat.Instance);
            await Assert.That(rendition.ContentType).IsEqualTo(rendition.Format == "webp" ? "image/webp" : "image/jpeg");
        }
    }

    /// <summary>A PNG keeps PNG as its fallback format; a WebP needs no second copy; a width at the image's own is a plain copy.</summary>
    [Test]
    public async Task CreateRenditions_PngAndWebp()
    {
        var png = await Processor.CreateRenditionsAsync(TestImages.SplitImage(300, 100, PngFormat.Instance), [300], CancellationToken.None);
        var webp = await Processor.CreateRenditionsAsync(TestImages.SplitImage(300, 100, WebpFormat.Instance), [150], CancellationToken.None);

        await Assert.That(png.Select(r => (r.Width, r.Height, r.Format))).IsEquivalentTo([(300, 100, "webp"), (300, 100, "png")]);
        await Assert.That(webp.Select(r => (r.Width, r.Height, r.Format))).IsEquivalentTo([(150, 50, "webp")]);
    }

    /// <summary>GIFs get no renditions, and widths wider than the image are ignored.</summary>
    [Test]
    public async Task CreateRenditions_SkipsGifsAndOversizedWidths()
    {
        var gif = await Processor.CreateRenditionsAsync(TestImages.SplitImage(400, 200, GifFormat.Instance), [320], CancellationToken.None);
        var tooWide = await Processor.CreateRenditionsAsync(TestImages.SplitImage(100, 50, PngFormat.Instance), [320], CancellationToken.None);

        await Assert.That(gif).IsEmpty();
        await Assert.That(tooWide).IsEmpty();
    }

    /// <summary>Renditions are current when they match the planned widths and formats of the current version.</summary>
    [Test]
    public async Task IsOutdated_ComparesWidthsFormatsAndVersion()
    {
        (int, string, string)[] current = [(320, "webp", "abc/v2/320.webp"), (320, "jpg", "abc/v2/320.jpg"), (400, "webp", "abc/v2/400.webp"), (400, "jpg", "abc/v2/400.jpg")];
        (int, string, string)[] oldVersion = [.. current.Select(r => (r.Item1, r.Item2, r.Item3.Replace("/v2/", "/v1/")))];

        await Assert.That(MediaRenditionWriter.IsOutdated(400, "image/jpeg", 2, "abc", current, DefaultWidths)).IsFalse();
        await Assert.That(MediaRenditionWriter.IsOutdated(400, "image/jpeg", 2, "abc", oldVersion, DefaultWidths)).IsTrue();
        await Assert.That(MediaRenditionWriter.IsOutdated(400, "image/jpeg", 2, "abc", [], DefaultWidths)).IsTrue();
        await Assert.That(MediaRenditionWriter.IsOutdated(400, "image/jpeg", 2, "abc", current, [200])).IsTrue();
        await Assert.That(MediaRenditionWriter.IsOutdated(400, "image/gif", 2, "abc", [], DefaultWidths)).IsFalse();
    }

    /// <summary>
    /// <c>?w=</c> picks the smallest rendition at least that wide (or the largest), <c>?f=webp</c> the WebP ones, and a
    /// missing format means none.
    /// </summary>
    [Test]
    public async Task ChooseRendition_PicksNearestWidthInFormat()
    {
        MediaEndpoints.MediaRenditionChoice[] renditions =
        [
            new(320, "webp", "a/v1/320.webp"), new(640, "webp", "a/v1/640.webp"),
            new(320, "jpg", "a/v1/320.jpg"), new(640, "jpg", "a/v1/640.jpg")
        ];

        await Assert.That(MediaEndpoints.ChooseRendition(renditions, 400, "webp")!.StorageKey).IsEqualTo("a/v1/640.webp");
        await Assert.That(MediaEndpoints.ChooseRendition(renditions, 300, null)!.StorageKey).IsEqualTo("a/v1/320.jpg");
        await Assert.That(MediaEndpoints.ChooseRendition(renditions, 5000, null)!.StorageKey).IsEqualTo("a/v1/640.jpg");
        await Assert.That(MediaEndpoints.ChooseRendition(renditions, null, "webp")!.StorageKey).IsEqualTo("a/v1/640.webp");
        await Assert.That(MediaEndpoints.ChooseRendition([], 640, "webp")).IsNull();
    }

    /// <summary>A WebP original only has WebP renditions, which also serve requests without <c>?f=</c>.</summary>
    [Test]
    public async Task ChooseRendition_WebpOriginal_ServesWebpForOwnFormat()
    {
        MediaEndpoints.MediaRenditionChoice[] renditions = [new(320, "webp", "a/v1/320.webp"), new(640, "webp", "a/v1/640.webp")];

        await Assert.That(MediaEndpoints.ChooseRendition(renditions, 320, null)!.StorageKey).IsEqualTo("a/v1/320.webp");
    }
}
