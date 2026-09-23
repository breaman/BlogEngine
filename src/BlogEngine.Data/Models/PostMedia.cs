namespace BlogEngine.Data.Models;

/// <summary>
/// Join row recording that a post references a media item (design 6.6). Rebuilt on every post save by
/// scanning the Markdown, and used for "Used in N posts" and the unused-media filter.
/// </summary>
/// <remarks>Join tables are exempt from fingerprinting, so this has no base class.</remarks>
public class PostMedia
{
    /// <summary>The referencing post.</summary>
    public int PostId { get; set; }

    /// <summary>The post.</summary>
    public Post Post { get; set; } = null!;

    /// <summary>The referenced media item.</summary>
    public int MediaItemId { get; set; }

    /// <summary>The media item.</summary>
    public MediaItem MediaItem { get; set; } = null!;
}
