namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Edits to apply to a media item's <b>original</b> (design 9.2): rotate and flip, then crop, then resize.
/// Stored as the item's <c>EditOperationsJson</c>, so every edit starts again from the untouched original.
/// </summary>
/// <example>
/// <code>
/// // Rotate a portrait photo to landscape, keep a 1200 × 800 area and scale it to 600 × 400.
/// new MediaEditOperations
/// {
///     Rotate = 90,
///     Crop = new MediaCropRect { X = 100, Y = 50, Width = 1200, Height = 800 },
///     Resize = new MediaSize { Width = 600, Height = 400 }
/// };
/// </code>
/// </example>
public sealed class MediaEditOperations
{
    /// <summary>Clockwise rotation in degrees: 0, 90, 180 or 270 (Q8).</summary>
    public int Rotate { get; set; }

    /// <summary>Mirror left to right, after rotating.</summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>Mirror top to bottom, after rotating.</summary>
    public bool FlipVertical { get; set; }

    /// <summary>
    /// Area to keep, in natural pixel coordinates of the rotated and flipped original; <see langword="null"/>
    /// keeps the whole image.
    /// </summary>
    public MediaCropRect? Crop { get; set; }

    /// <summary>Final size, no larger than the cropped area; <see langword="null"/> keeps the cropped size.</summary>
    public MediaSize? Resize { get; set; }

    /// <summary>Whether the operations leave the original unchanged.</summary>
    public bool IsIdentity => Rotate % 360 == 0 && !FlipHorizontal && !FlipVertical && Crop is null && Resize is null;
}

/// <summary>A rectangle in natural pixel coordinates.</summary>
public sealed class MediaCropRect
{
    /// <summary>Left edge.</summary>
    public int X { get; set; }

    /// <summary>Top edge.</summary>
    public int Y { get; set; }

    /// <summary>Width, at least 1.</summary>
    public int Width { get; set; }

    /// <summary>Height, at least 1.</summary>
    public int Height { get; set; }
}

/// <summary>A size in pixels.</summary>
public sealed class MediaSize
{
    /// <summary>Width, at least 1.</summary>
    public int Width { get; set; }

    /// <summary>Height, at least 1.</summary>
    public int Height { get; set; }
}
