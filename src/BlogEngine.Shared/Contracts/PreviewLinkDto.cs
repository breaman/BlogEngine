namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A private, expiring preview link for an unpublished post (design 6.7, 7.1, A14): <c>/preview/{token}</c>.
/// </summary>
public sealed class PreviewLinkDto
{
    /// <summary>The link id, used to revoke it.</summary>
    public int Id { get; set; }

    /// <summary>The 256-bit random token, base64url-encoded.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The site-relative preview URL, such as <c>/preview/3q2-7w…</c>.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>When the link stops working (UTC).</summary>
    public DateTimeOffset ExpiresOn { get; set; }

    /// <summary>When the link was created (UTC).</summary>
    public DateTimeOffset? CreatedOn { get; set; }
}