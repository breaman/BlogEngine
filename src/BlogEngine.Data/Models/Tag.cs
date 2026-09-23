namespace BlogEngine.Data.Models;

/// <summary>
/// A post tag (design 6.4). Names are matched case-insensitively through <see cref="NormalizedName"/>,
/// so <c>C#</c> and <c>c#</c> are the same tag and the first-entered casing is kept for display.
/// </summary>
public class Tag : FingerPrintEntityBase
{
    /// <summary>Display name as first entered, for example <c>C#</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-cased, NFKC-normalized name; unique, and the final guard against duplicates.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>URL slug used in <c>/tags/{slug}</c>, for example <c>csharp</c>; unique.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Optional description shown on the tag page.</summary>
    public string? Description { get; set; }

    /// <summary>Posts with this tag (skip navigation through <see cref="PostTags"/>).</summary>
    public List<Post> Posts { get; set; } = [];

    /// <summary>Join rows linking this tag to its posts.</summary>
    public List<PostTag> PostTags { get; set; } = [];
}
