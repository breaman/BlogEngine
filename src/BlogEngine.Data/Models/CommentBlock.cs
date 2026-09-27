using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Models;

/// <summary>
/// A blocked commenter or blocked content pattern consulted by the spam guard (design 6.5, 8.3).
/// </summary>
/// <remarks>The creation time comes from <see cref="FingerPrintEntityBase.CreatedOn"/>.</remarks>
public class CommentBlock : FingerPrintEntityBase
{
    /// <summary>What <see cref="Value"/> is matched against.</summary>
    public CommentBlockKind Kind { get; set; }

    /// <summary>The email, IP hash, keyword or domain to block.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional note explaining the block.</summary>
    public string? Note { get; set; }
}