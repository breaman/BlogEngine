namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Body of <c>POST /api/admin/posts/{id}/preview-token</c> (design 7.4, A14).
/// </summary>
public sealed class CreatePreviewLinkRequest
{
    /// <summary>Default lifetime of a preview link (design 6.7).</summary>
    public const int DefaultExpiresInDays = 7;

    /// <summary>Longest lifetime a link may be given.</summary>
    public const int MaxExpiresInDays = 90;

    /// <summary>How many days the link works for.</summary>
    public int ExpiresInDays { get; set; } = DefaultExpiresInDays;
}