namespace BlogEngine.Data.Models;

/// <summary>
/// Join row between <see cref="Post"/> and <see cref="Tag"/>, keyed by both IDs (design 6.4).
/// </summary>
/// <remarks>Join tables are exempt from fingerprinting, so this has no base class.</remarks>
public class PostTag
{
    /// <summary>The tagged post.</summary>
    public int PostId { get; set; }

    /// <summary>The post.</summary>
    public Post Post { get; set; } = null!;

    /// <summary>The tag.</summary>
    public int TagId { get; set; }

    /// <summary>The tag.</summary>
    public Tag Tag { get; set; } = null!;
}