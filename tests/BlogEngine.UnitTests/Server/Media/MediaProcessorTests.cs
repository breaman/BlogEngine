using System.Text;

using BlogEngine.Server.Services.Media;
using BlogEngine.Shared.Contracts;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace BlogEngine.UnitTests.Server.Media;

/// <summary>
/// Tests <see cref="MediaProcessor"/> (design 9.1, 9.2, T2.2): orientation is baked into the pixels, GPS and other
/// metadata are gone, only real JPEG/PNG/GIF/WebP images are accepted, and edit operations produce the expected
/// dimensions in the order rotate/flip, crop, resize.
/// </summary>
public class MediaProcessorTests
{
    private static readonly MediaProcessor Processor = new();

    /// <summary>A sideways phone photo comes out upright: the EXIF rotation is applied to the pixels.</summary>
    [Test]
    public async Task ProcessUpload_AppliesExifOrientation()
    {
        var result = await ProcessAsync(TestImages.SidewaysPhotoWithGps(400, 200));

        await Assert.That((result.Width, result.Height)).IsEqualTo((200, 400));
        using var image = Image.Load<Rgba32>(result.Content);
        // Orientation 6 turns the stored left half (red) to the top.
        await Assert.That(TestImages.IsRed(image[100, 50])).IsTrue();
        await Assert.That(TestImages.IsBlue(image[100, 350])).IsTrue();
    }

    /// <summary>EXIF (with the GPS location), IPTC and XMP are removed, including the orientation tag.</summary>
    [Test]
    public async Task ProcessUpload_StripsGpsAndOtherMetadata()
    {
        var result = await ProcessAsync(TestImages.SidewaysPhotoWithGps());

        var info = Image.Identify(result.Content);
        await Assert.That(info.Metadata.ExifProfile).IsNull();
        await Assert.That(info.Metadata.IptcProfile).IsNull();
        await Assert.That(info.Metadata.XmpProfile).IsNull();
        await Assert.That(ContainsAscii(result.Content, "PhoneMaker")).IsFalse();
    }

    /// <summary>Each accepted format is kept, with its media type and extension.</summary>
    [Test]
    public async Task ProcessUpload_KeepsAcceptedFormats()
    {
        var cases = new (byte[] Bytes, string ContentType, string Extension)[]
        {
            (TestImages.SplitImage(20, 10, JpegFormat.Instance), "image/jpeg", "jpg"),
            (TestImages.SplitImage(20, 10, PngFormat.Instance), "image/png", "png"),
            (TestImages.SplitImage(20, 10, GifFormat.Instance), "image/gif", "gif"),
            (TestImages.SplitImage(20, 10, WebpFormat.Instance), "image/webp", "webp")
        };

        foreach (var (bytes, contentType, extension) in cases)
        {
            var result = await ProcessAsync(bytes);

            await Assert.That(result.ContentType).IsEqualTo(contentType);
            await Assert.That(result.Extension).IsEqualTo(extension);
            await Assert.That((result.Width, result.Height)).IsEqualTo((20, 10));
        }
    }

    /// <summary>The hash is the lowercase hex SHA-256 of the stored bytes, and the same input gives the same hash.</summary>
    [Test]
    public async Task ProcessUpload_HashIsStableSha256()
    {
        var bytes = TestImages.SplitImage(30, 30, PngFormat.Instance);

        var first = await ProcessAsync(bytes);
        var second = await ProcessAsync(bytes);

        await Assert.That(first.Hash).Matches("^[0-9a-f]{64}$");
        await Assert.That(first.Hash).IsEqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(first.Content)));
        await Assert.That(second.Hash).IsEqualTo(first.Hash);
    }

    /// <summary>Originals larger than the setting are shrunk to fit, keeping the aspect ratio.</summary>
    [Test]
    public async Task ProcessUpload_DownscalesLargeOriginals()
    {
        var result = await ProcessAsync(TestImages.SplitImage(400, 100, PngFormat.Instance), downscaleAbove: 200);

        await Assert.That((result.Width, result.Height)).IsEqualTo((200, 50));
    }

    /// <summary>A zero setting keeps the full size.</summary>
    [Test]
    public async Task ProcessUpload_ZeroDownscaleKeepsSize()
    {
        var result = await ProcessAsync(TestImages.SplitImage(400, 100, PngFormat.Instance), downscaleAbove: 0);

        await Assert.That((result.Width, result.Height)).IsEqualTo((400, 100));
    }

    /// <summary>A text file renamed to .jpg is identified by its content and rejected.</summary>
    [Test]
    public async Task ProcessUpload_NonImage_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<MediaProcessingException>(() => ProcessAsync(Encoding.UTF8.GetBytes("Just some text, not a photo.")));

        await Assert.That(exception!.Message).Contains("isn't an image");
    }

    /// <summary>HEIC photos get a message explaining how to export JPEG instead (Q6).</summary>
    [Test]
    public async Task ProcessUpload_Heic_IsRejectedWithHelpfulMessage()
    {
        // An ISO base media header: box size, "ftyp", then the "heic" brand.
        var heic = new byte[64];
        heic[3] = 0x18;
        "ftypheic"u8.CopyTo(heic.AsSpan(4));
        "mif1heic"u8.CopyTo(heic.AsSpan(16));

        var exception = await Assert.ThrowsAsync<MediaProcessingException>(() => ProcessAsync(heic));

        await Assert.That(exception!.Message).Contains("HEIC");
    }

    /// <summary>SVG is rejected because it can carry scripts (design 12.2).</summary>
    [Test]
    public async Task ProcessUpload_Svg_IsRejected()
    {
        var svg = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        var exception = await Assert.ThrowsAsync<MediaProcessingException>(() => ProcessAsync(svg));

        await Assert.That(exception!.Message).Contains("SVG");
    }

    /// <summary>Formats ImageSharp can read but the library doesn't accept, such as BMP, are rejected.</summary>
    [Test]
    public async Task ProcessUpload_Bmp_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<MediaProcessingException>(() => ProcessAsync(TestImages.SplitImage(10, 10, BmpFormat.Instance)));

        await Assert.That(exception!.Message).IsEqualTo(MediaProcessor.UnsupportedFormatMessage);
    }

    /// <summary>Rotate, then crop in the rotated image's coordinates, then resize.</summary>
    [Test]
    public async Task ApplyEdits_RotateCropResize_ProducesExpectedDimensions()
    {
        var original = TestImages.SplitImage(400, 200, PngFormat.Instance);
        var operations = new MediaEditOperations
        {
            Rotate = 90,
            Crop = new MediaCropRect { X = 0, Y = 0, Width = 200, Height = 200 },
            Resize = new MediaSize { Width = 100, Height = 100 }
        };

        var result = await EditAsync(original, operations);

        await Assert.That((result.Width, result.Height)).IsEqualTo((100, 100));
        using var image = Image.Load<Rgba32>(result.Content);
        // After a clockwise quarter turn the red (left) half is on top, so the top square is all red.
        await Assert.That(TestImages.IsRed(image[50, 50])).IsTrue();
    }

    /// <summary>Rotation alone swaps width and height.</summary>
    [Test]
    [Arguments(90, 200, 400)]
    [Arguments(180, 400, 200)]
    [Arguments(270, 200, 400)]
    public async Task ApplyEdits_Rotation_SetsDimensions(int rotate, int width, int height)
    {
        var result = await EditAsync(TestImages.SplitImage(400, 200, PngFormat.Instance), new MediaEditOperations { Rotate = rotate });

        await Assert.That((result.Width, result.Height)).IsEqualTo((width, height));
    }

    /// <summary>A horizontal flip mirrors left and right.</summary>
    [Test]
    public async Task ApplyEdits_FlipHorizontal_MirrorsImage()
    {
        var result = await EditAsync(TestImages.SplitImage(40, 20, PngFormat.Instance), new MediaEditOperations { FlipHorizontal = true });

        using var image = Image.Load<Rgba32>(result.Content);
        await Assert.That(TestImages.IsBlue(image[5, 10])).IsTrue();
        await Assert.That(TestImages.IsRed(image[35, 10])).IsTrue();
    }

    /// <summary>A flip applies after the rotation: rotating 90° then flipping vertically puts red at the bottom.</summary>
    [Test]
    public async Task ApplyEdits_FlipAppliesAfterRotation()
    {
        var result = await EditAsync(TestImages.SplitImage(40, 20, PngFormat.Instance), new MediaEditOperations { Rotate = 90, FlipVertical = true });

        using var image = Image.Load<Rgba32>(result.Content);
        await Assert.That(TestImages.IsBlue(image[10, 5])).IsTrue();
        await Assert.That(TestImages.IsRed(image[10, 35])).IsTrue();
    }

    /// <summary>A crop outside the rotated image is refused rather than clamped silently.</summary>
    [Test]
    public async Task ApplyEdits_CropOutsideImage_IsRejected()
    {
        var operations = new MediaEditOperations { Rotate = 90, Crop = new MediaCropRect { X = 0, Y = 0, Width = 300, Height = 100 } };

        var exception = await Assert.ThrowsAsync<MediaProcessingException>(() => EditAsync(TestImages.SplitImage(400, 200, PngFormat.Instance), operations));

        await Assert.That(exception!.Message).Contains("200 × 400");
    }

    /// <summary>Resizing can shrink but never enlarge the cropped area.</summary>
    [Test]
    public async Task ApplyEdits_Upscale_IsRejected()
    {
        var operations = new MediaEditOperations
        {
            Crop = new MediaCropRect { X = 0, Y = 0, Width = 100, Height = 100 },
            Resize = new MediaSize { Width = 150, Height = 150 }
        };

        await Assert.ThrowsAsync<MediaProcessingException>(() => EditAsync(TestImages.SplitImage(400, 200, PngFormat.Instance), operations));
    }

    /// <summary>Editing keeps the format and never brings metadata back.</summary>
    [Test]
    public async Task ApplyEdits_KeepsFormatWithoutMetadata()
    {
        var original = await ProcessAsync(TestImages.SidewaysPhotoWithGps());

        var result = await EditAsync(original.Content, new MediaEditOperations { Rotate = 180 });

        await Assert.That(result.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(Image.Identify(result.Content).Metadata.ExifProfile).IsNull();
    }

    private static async Task<ProcessedImage> ProcessAsync(byte[] bytes, int downscaleAbove = 0)
    {
        using var input = new MemoryStream(bytes);
        return await Processor.ProcessUploadAsync(input, downscaleAbove, CancellationToken.None);
    }

    private static async Task<ProcessedImage> EditAsync(byte[] original, MediaEditOperations operations)
    {
        using var input = new MemoryStream(original);
        return await Processor.ApplyEditsAsync(input, operations, CancellationToken.None);
    }

    private static bool ContainsAscii(byte[] bytes, string text)
    {
        return bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;
    }
}
