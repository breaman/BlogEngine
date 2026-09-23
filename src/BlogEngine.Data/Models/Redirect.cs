namespace BlogEngine.Data.Models;

/// <summary>
/// A permanent redirect from an old URL path (design 6.7). Created automatically when a published
/// post's slug or date changes, or when tags are merged.
/// </summary>
/// <remarks>
/// Deliberately not linked to a post by foreign key: tag merges also create redirects, and a redirect
/// should outlive changes to whatever it points at.
/// </remarks>
public class Redirect : FingerPrintEntityBase
{
    /// <summary>Old path, such as <c>/posts/2026/09/22/old-slug</c>; unique.</summary>
    public string FromPath { get; set; } = string.Empty;

    /// <summary>Path to redirect to.</summary>
    public string ToPath { get; set; } = string.Empty;

    /// <summary>HTTP status code of the redirect.</summary>
    public int StatusCode { get; set; } = 301;

    /// <summary>How many times the redirect has been followed.</summary>
    public int HitCount { get; set; }
}
