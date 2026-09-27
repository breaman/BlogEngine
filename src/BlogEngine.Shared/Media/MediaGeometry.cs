using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Media;

/// <summary>
/// Pixel arithmetic shared by the image editor (WebAssembly) and the server that applies the edits (design 9.2),
/// so both agree on sizes and coordinates.
/// </summary>
public static class MediaGeometry
{
    /// <summary>Largest width or height an edit may produce.</summary>
    public const int MaxDimension = 20000;

    /// <summary>The size of a <paramref name="width"/> × <paramref name="height"/> image after a clockwise rotation.</summary>
    /// <example>
    /// <code>
    /// var (w, h) = MediaGeometry.RotatedSize(4000, 3000, 90); // (3000, 4000)
    /// </code>
    /// </example>
    public static (int Width, int Height) RotatedSize(int width, int height, int rotate)
    {
        return NormalizeRotation(rotate) is 90 or 270 ? (height, width) : (width, height);
    }

    /// <summary>A rotation in degrees as 0, 90, 180 or 270; other angles are rounded to the nearest quarter turn.</summary>
    public static int NormalizeRotation(int degrees)
    {
        var quarterTurns = (int)Math.Round(degrees / 90.0, MidpointRounding.AwayFromZero);
        return ((quarterTurns % 4) + 4) % 4 * 90;
    }

    /// <summary>
    /// Maps a rectangle drawn over a scaled-down display of an image to the image's natural pixels, clamped to the
    /// image (design 9.2, T2.9).
    /// </summary>
    /// <param name="selection">The rectangle, in the same coordinate space as <paramref name="displayedImage"/>.</param>
    /// <param name="displayedImage">Where the image is drawn, in the same coordinate space.</param>
    /// <param name="naturalWidth">Width of the image in natural pixels (after rotation).</param>
    /// <param name="naturalHeight">Height of the image in natural pixels (after rotation).</param>
    /// <returns>The rectangle in natural pixels, at least 1 × 1 and inside the image.</returns>
    /// <example>
    /// <code>
    /// // An image shown at half size, offset 10 px: the selection's top-left quarter maps to (0, 0, 2000, 1500).
    /// MediaGeometry.ToNatural(new(10, 20, 1000, 750), new(10, 20, 2000, 1500), 4000, 3000);
    /// </code>
    /// </example>
    public static MediaCropRect ToNatural(DisplayRect selection, DisplayRect displayedImage, int naturalWidth, int naturalHeight)
    {
        if (displayedImage.Width <= 0 || displayedImage.Height <= 0 || naturalWidth <= 0 || naturalHeight <= 0)
        {
            return new MediaCropRect { X = 0, Y = 0, Width = Math.Max(1, naturalWidth), Height = Math.Max(1, naturalHeight) };
        }

        var scaleX = naturalWidth / displayedImage.Width;
        var scaleY = naturalHeight / displayedImage.Height;

        // Round the edges rather than the size, so adjoining rectangles don't gain or lose a pixel.
        var left = Clamp((int)Math.Round((selection.X - displayedImage.X) * scaleX), 0, naturalWidth - 1);
        var top = Clamp((int)Math.Round((selection.Y - displayedImage.Y) * scaleY), 0, naturalHeight - 1);
        var right = Clamp((int)Math.Round((selection.X + selection.Width - displayedImage.X) * scaleX), left + 1, naturalWidth);
        var bottom = Clamp((int)Math.Round((selection.Y + selection.Height - displayedImage.Y) * scaleY), top + 1, naturalHeight);

        return new MediaCropRect { X = left, Y = top, Width = right - left, Height = bottom - top };
    }

    /// <summary>
    /// Adjusts <paramref name="crop"/> to <paramref name="aspectRatio"/> (width / height) as closely as whole pixels allow, keeping its center
    /// and staying inside the image. Screen selections are only accurate to a display pixel, which at a reduced
    /// display size is several natural pixels, so an aspect-locked selection would otherwise come out as 1000 × 1002.
    /// </summary>
    /// <example>
    /// <code>
    /// var square = MediaGeometry.FitAspectRatio(new() { X = 0, Y = 301, Width = 1000, Height = 1002 }, 1, 1000, 1600);
    /// // 1000 × 1000 at (0, 302)
    /// </code>
    /// </example>
    public static MediaCropRect FitAspectRatio(MediaCropRect crop, double aspectRatio, int naturalWidth, int naturalHeight)
    {
        ArgumentNullException.ThrowIfNull(crop);
        if (aspectRatio <= 0 || double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio))
        {
            return crop;
        }

        // Keep the width unless the matching height doesn't fit; then derive the width from the height.
        var width = Clamp(crop.Width, 1, naturalWidth);
        var height = (int)Math.Round(width / aspectRatio);
        if (height > naturalHeight || height < 1)
        {
            height = Clamp(Math.Min(crop.Height, naturalHeight), 1, naturalHeight);
            width = Clamp((int)Math.Round(height * aspectRatio), 1, naturalWidth);
        }

        var centerX = crop.X + (crop.Width / 2.0);
        var centerY = crop.Y + (crop.Height / 2.0);
        var x = Clamp((int)Math.Round(centerX - (width / 2.0)), 0, naturalWidth - width);
        var y = Clamp((int)Math.Round(centerY - (height / 2.0)), 0, naturalHeight - height);

        return new MediaCropRect { X = x, Y = y, Width = width, Height = height };
    }

    /// <summary>
    /// The size that fits <paramref name="requestedWidth"/> with the aspect ratio of <paramref name="bounds"/>, never
    /// larger than <paramref name="bounds"/> (no upscaling).
    /// </summary>
    public static MediaSize ScaleToWidth(MediaSize bounds, int requestedWidth)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        var width = Clamp(requestedWidth, 1, bounds.Width);
        var height = Clamp((int)Math.Round(width * (double)bounds.Height / bounds.Width), 1, bounds.Height);
        return new MediaSize { Width = width, Height = height };
    }

    /// <summary>
    /// The size that fits <paramref name="requestedHeight"/> with the aspect ratio of <paramref name="bounds"/>, never
    /// larger than <paramref name="bounds"/> (no upscaling).
    /// </summary>
    public static MediaSize ScaleToHeight(MediaSize bounds, int requestedHeight)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        var height = Clamp(requestedHeight, 1, bounds.Height);
        var width = Clamp((int)Math.Round(height * (double)bounds.Width / bounds.Height), 1, bounds.Width);
        return new MediaSize { Width = width, Height = height };
    }

    /// <summary>
    /// Checks <paramref name="operations"/> against an original of <paramref name="originalWidth"/> ×
    /// <paramref name="originalHeight"/>: the crop must lie inside the rotated image and the resize must not
    /// upscale. Returns the error message, or <see langword="null"/> when they fit.
    /// </summary>
    public static string? CheckFits(MediaEditOperations operations, int originalWidth, int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var (width, height) = RotatedSize(originalWidth, originalHeight, operations.Rotate);
        if (operations.Crop is { } crop)
        {
            if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1
                || crop.X + crop.Width > width || crop.Y + crop.Height > height)
            {
                return $"The crop area must lie inside the {width} × {height} image.";
            }

            (width, height) = (crop.Width, crop.Height);
        }

        if (operations.Resize is { } resize && (resize.Width > width || resize.Height > height))
        {
            return $"The image can't be enlarged beyond {width} × {height}.";
        }

        return null;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), Math.Max(min, max));
    }
}

/// <summary>A rectangle in display (CSS pixel) coordinates.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct DisplayRect(double X, double Y, double Width, double Height);