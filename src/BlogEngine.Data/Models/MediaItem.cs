namespace BlogEngine.Data.Models;

/// <summary>
/// An image in the media library (design 6.6). The original is never modified; edits are stored as
/// operations and applied to produce the current version.
/// </summary>
public class MediaItem : FingerPrintEntityBase
{
    /// <summary>Short random ID used in URLs, so URLs don't reveal the row count; unique.</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>Slugified original file name used in the URL, for example <c>sunset-at-lake.jpg</c>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Storage key of the original upload, after orientation fix and metadata removal.</summary>
    public string OriginalStorageKey { get; set; } = string.Empty;

    /// <summary>Storage key of the result of applying <see cref="EditOperationsJson"/> to the original.</summary>
    public string CurrentStorageKey { get; set; } = string.Empty;

    /// <summary>Edit operations (rotate, crop, resize) as JSON; <see langword="null"/> when unedited.</summary>
    public string? EditOperationsJson { get; set; }

    /// <summary>Media type detected by decoding the file, not from its extension.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Width of the current version, in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height of the current version, in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Size of the current version, in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Alternative text; the editor prompts for it on insert.</summary>
    public string AltText { get; set; } = string.Empty;

    /// <summary>Optional caption, rendered as <c>&lt;figcaption&gt;</c>.</summary>
    public string? Caption { get; set; }

    /// <summary>Lowercase hex SHA-256 of the original, used for duplicate detection.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Incremented on every edit and appended to URLs for cache busting.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Pre-generated resized copies.</summary>
    public List<MediaRendition> Renditions { get; set; } = [];

    /// <summary>Posts whose content references this item.</summary>
    public List<PostMedia> PostMedia { get; set; } = [];
}