using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace BlogEngine.UnitTests.Server.Media;

/// <summary>
/// Fixture images generated with ImageSharp, so tests don't depend on binary files: a two-color image whose left
/// half is red and right half blue shows where pixels went after rotating or flipping.
/// </summary>
internal static class TestImages
{
    /// <summary>A GPS latitude that must never survive an upload.</summary>
    public static readonly Rational[] Latitude = [new(47, 1), new(36, 1), new(3, 1)];

    /// <summary>Encodes a red/blue split image in <paramref name="format"/>.</summary>
    public static byte[] SplitImage(int width, int height, IImageFormat format, Action<Image>? configure = null)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = x < width / 2 ? Color.Red : Color.Blue;
            }
        }

        configure?.Invoke(image);

        using var output = new MemoryStream();
        image.Save(output, format switch
        {
            JpegFormat => new JpegEncoder { Quality = 95 },
            PngFormat => new PngEncoder(),
            GifFormat => new GifEncoder(),
            WebpFormat => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
            BmpFormat => new BmpEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        });
        return output.ToArray();
    }

    /// <summary>
    /// A JPEG as a phone takes it: stored sideways with EXIF orientation 6 ("rotate 90° clockwise to display") and a
    /// GPS location.
    /// </summary>
    public static byte[] SidewaysPhotoWithGps(int storedWidth = 400, int storedHeight = 200)
    {
        return SplitImage(storedWidth, storedHeight, JpegFormat.Instance, image =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, Latitude);
            exif.SetValue(ExifTag.Make, "PhoneMaker");
            image.Metadata.ExifProfile = exif;
        });
    }

    /// <summary>Whether a pixel is mostly red (lossy formats blur the exact color).</summary>
    public static bool IsRed(Rgba32 pixel)
    {
        return pixel.R > 200 && pixel.B < 80;
    }

    /// <summary>Whether a pixel is mostly blue.</summary>
    public static bool IsBlue(Rgba32 pixel)
    {
        return pixel.B > 200 && pixel.R < 80;
    }
}