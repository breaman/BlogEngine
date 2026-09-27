using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A blocked commenter or blocked content pattern (design 6.5, 8.3), for the blocklist in the admin comments page.
/// </summary>
public sealed class CommentBlockDto
{
    /// <summary>The block id.</summary>
    public int Id { get; set; }

    /// <summary>What <see cref="Value"/> is matched against.</summary>
    public CommentBlockKind Kind { get; set; }

    /// <summary>The blocked email, IP hash, keyword or domain.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional note explaining the block.</summary>
    public string? Note { get; set; }

    /// <summary>When the block was added.</summary>
    public DateTimeOffset? CreatedOn { get; set; }
}