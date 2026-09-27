namespace BlogEngine.Data.Models;

/// <summary>
/// A pre-generated resized copy of a <see cref="MediaItem"/> at one width and format (design 6.6, 9.4).
/// </summary>
public class MediaRendition : FingerPrintEntityBase
{
    /// <summary>The source media item.</summary>
    public int MediaItemId { get; set; }

    /// <summary>The media item.</summary>
    public MediaItem MediaItem { get; set; } = null!;

    /// <summary>Rendition width, in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Encoding, for example <c>webp</c>.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Storage key of the rendition.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Size of the rendition, in bytes.</summary>
    public long SizeBytes { get; set; }
}