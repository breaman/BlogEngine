namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A media library item as the admin area shows it (design 6.6, 7.4).
/// </summary>
public sealed class MediaItemDto
{
    /// <summary>The item id.</summary>
    public int Id { get; set; }

    /// <summary>Short random id used in the public URL.</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>Slugified file name used in the public URL, for example <c>sunset-at-lake.jpg</c>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Public URL of the current version, with the cache-busting <c>?v=</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Unversioned public path, as inserted into Markdown.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Media type detected by decoding the file, such as <c>image/jpeg</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Width of the current version, in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height of the current version, in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Size of the current version, in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Alternative text.</summary>
    public string AltText { get; set; } = string.Empty;

    /// <summary>Optional caption.</summary>
    public string? Caption { get; set; }

    /// <summary>Incremented on every edit.</summary>
    public int Version { get; set; }

    /// <summary>When the item was uploaded.</summary>
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>The edits applied to the original; <see langword="null"/> when the item is unedited.</summary>
    public MediaEditOperations? EditOperations { get; set; }

    /// <summary>Number of posts (including posts in the trash) that use the item.</summary>
    public int UsageCount { get; set; }

    /// <summary>Widths of the responsive renditions of the current version (design 9.4), smallest first.</summary>
    public List<int> RenditionWidths { get; set; } = [];

    /// <summary>The posts that use the item; filled when a single item is loaded, empty in lists.</summary>
    public List<MediaUsageDto> UsedIn { get; set; } = [];
}
