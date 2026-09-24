using System.Security.Cryptography;
using System.Text;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Media;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BlogEngine.Server.Services.Media;

/// <summary>
/// Decodes, cleans up and edits images with ImageSharp (SixLabors.ImageSharp, design 5.3, 9.1, 9.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Uploads</b> (<see cref="ProcessUploadAsync"/>): the file is identified by decoding it, never by its name or
/// declared type; only JPEG, PNG, GIF and WebP are accepted (Q6). The EXIF orientation is applied to the pixels,
/// then the EXIF, IPTC and XMP metadata are removed, which takes GPS locations out of phone photos (M5). Very
/// large originals can be downscaled. The result is re-encoded in its own format.
/// </para>
/// <para>
/// <b>Edits</b> (<see cref="ApplyEditsAsync"/>): operations are applied to the stored original in a fixed order:
/// rotate and flip, then crop, then resize. The browser only sends the operations, never pixels.
/// </para>
/// <para>
/// ICC color profiles are kept, so wide-gamut photos keep their colors. Stateless and thread-safe: a singleton.
/// </para>
/// </remarks>
public sealed class MediaProcessor
{
    /// <summary>
    /// Largest image accepted, in pixels (100 megapixels). Checked from the header before decoding, so a small file
    /// that claims enormous dimensions (a decompression bomb) is rejected without allocating the pixels.
    /// </summary>
    public const long MaxPixels = 100_000_000;

    /// <summary>The formats the library accepts (Q6).</summary>
    private static readonly IReadOnlyList<IImageFormat> AllowedFormats =
        [JpegFormat.Instance, PngFormat.Instance, GifFormat.Instance, WebpFormat.Instance];

    /// <summary>Message for anything that isn't one of the accepted formats.</summary>
    public const string UnsupportedFormatMessage = "Only JPEG, PNG, GIF and WebP images can be uploaded.";

    /// <summary>
    /// Processes an upload: identifies it by decoding, applies the EXIF orientation, strips metadata and optionally
    /// downscales it.
    /// </summary>
    /// <param name="content">The uploaded bytes; read from its current position.</param>
    /// <param name="downscaleAbovePixels">
    /// Shrink the image so neither side exceeds this many pixels; 0 keeps the full size.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="MediaProcessingException">The file isn't an accepted image.</exception>
    public async Task<ProcessedImage> ProcessUploadAsync(Stream content, int downscaleAbovePixels, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var buffer = await BufferAsync(content, cancellationToken);
        var format = await DetectAllowedFormatAsync(buffer, cancellationToken);

        using var image = await LoadAsync(buffer, cancellationToken);
        image.Mutate(x => x.AutoOrient());

        if (downscaleAbovePixels > 0 && (image.Width > downscaleAbovePixels || image.Height > downscaleAbovePixels))
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(downscaleAbovePixels, downscaleAbovePixels)
            }));
        }

        return await EncodeAsync(image, format, cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="operations"/> to an original produced by <see cref="ProcessUploadAsync"/>.
    /// </summary>
    /// <exception cref="MediaProcessingException">
    /// The crop doesn't fit the image, the resize would enlarge it, or the original can't be decoded.
    /// </exception>
    public async Task<ProcessedImage> ApplyEditsAsync(Stream original, MediaEditOperations operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(operations);

        await using var buffer = await BufferAsync(original, cancellationToken);
        var format = await DetectAllowedFormatAsync(buffer, cancellationToken);

        using var image = await LoadAsync(buffer, cancellationToken);
        if (MediaGeometry.CheckFits(operations, image.Width, image.Height) is { } problem)
        {
            throw new MediaProcessingException(problem);
        }

        image.Mutate(x =>
        {
            switch (MediaGeometry.NormalizeRotation(operations.Rotate))
            {
                case 90:
                    x.Rotate(RotateMode.Rotate90);
                    break;
                case 180:
                    x.Rotate(RotateMode.Rotate180);
                    break;
                case 270:
                    x.Rotate(RotateMode.Rotate270);
                    break;
            }

            if (operations.FlipHorizontal)
            {
                x.Flip(FlipMode.Horizontal);
            }

            if (operations.FlipVertical)
            {
                x.Flip(FlipMode.Vertical);
            }

            if (operations.Crop is { } crop)
            {
                x.Crop(new Rectangle(crop.X, crop.Y, crop.Width, crop.Height));
            }

            if (operations.Resize is { } resize && (resize.Width != x.GetCurrentSize().Width || resize.Height != x.GetCurrentSize().Height))
            {
                x.Resize(resize.Width, resize.Height);
            }
        });

        return await EncodeAsync(image, format, cancellationToken);
    }

    /// <summary>Copies the input into memory so it can be read more than once (identify, then decode).</summary>
    private static async Task<MemoryStream> BufferAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Identifies the format from the file's content and checks its size, with a specific message for the formats
    /// authors most often try (HEIC from iPhones, SVG).
    /// </summary>
    private static async Task<IImageFormat> DetectAllowedFormatAsync(MemoryStream buffer, CancellationToken cancellationToken)
    {
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(buffer, cancellationToken);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new MediaProcessingException(DescribeUnknownFormat(buffer), ex);
        }
        finally
        {
            buffer.Position = 0;
        }

        var format = info.Metadata.DecodedImageFormat;
        if (format is null || !AllowedFormats.Contains(format))
        {
            throw new MediaProcessingException(UnsupportedFormatMessage);
        }

        if ((long)info.Width * info.Height > MaxPixels)
        {
            throw new MediaProcessingException($"The image is {info.Width} × {info.Height} pixels, which is too large to process.");
        }

        return format;
    }

    /// <summary>Decodes the image, reporting corrupt data as a <see cref="MediaProcessingException"/>.</summary>
    private static async Task<Image> LoadAsync(MemoryStream buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await Image.LoadAsync(buffer, cancellationToken);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new MediaProcessingException("The image is damaged and can't be read.", ex);
        }
    }

    /// <summary>Explains why a file that ImageSharp can't identify was rejected.</summary>
    private static string DescribeUnknownFormat(MemoryStream buffer)
    {
        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 512));

        // ISO base media files ("ftyp" at offset 4) with a HEIF brand: iPhone photos.
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(header.Slice(8, 4));
            if (brand is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1" or "avif")
            {
                return "HEIC/HEIF photos aren't supported. Export the photo as JPEG (on iPhone: Settings → Camera → Formats → Most Compatible) and upload it again.";
            }
        }

        var text = Encoding.UTF8.GetString(header).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
        {
            return "SVG images aren't supported, because they can contain scripts. Upload a PNG instead.";
        }

        return $"The file isn't an image. {UnsupportedFormatMessage}";
    }

    /// <summary>Removes metadata that can identify people or places, then encodes and hashes the image.</summary>
    private static async Task<ProcessedImage> EncodeAsync(Image image, IImageFormat format, CancellationToken cancellationToken)
    {
        StripMetadata(image);

        using var output = new MemoryStream();
        await image.SaveAsync(output, format, cancellationToken);
        var bytes = output.ToArray();

        return new ProcessedImage(
            bytes,
            format.DefaultMimeType,
            format.FileExtensions.First(),
            image.Width,
            image.Height,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Drops EXIF (including GPS), IPTC and XMP from the image and every frame, plus the free-text comments some
    /// formats carry. The ICC profile stays, because removing it would shift colors.
    /// </summary>
    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.IptcProfile = null;
            frame.Metadata.XmpProfile = null;
        }

        image.Metadata.GetPngMetadata().TextData.Clear();
        image.Metadata.GetGifMetadata().Comments.Clear();
    }
}
