namespace BlogEngine.Data.Models;

/// <summary>
/// A private, expiring link that lets someone preview an unpublished post at <c>/preview/{token}</c>
/// (design 6.7, 12.2). Revoking a link deletes its row.
/// </summary>
public class PreviewToken : FingerPrintEntityBase
{
    /// <summary>The post being previewed.</summary>
    public int PostId { get; set; }

    /// <summary>The post.</summary>
    public Post Post { get; set; } = null!;

    /// <summary>256-bit random value, base64url-encoded; unique.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>When the link stops working.</summary>
    public DateTimeOffset ExpiresOn { get; set; }
}