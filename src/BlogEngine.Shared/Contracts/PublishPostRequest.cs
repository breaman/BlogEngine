namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Body of <c>POST /api/admin/posts/{id}/publish</c> (design 6.3, 7.4).
/// </summary>
public sealed class PublishPostRequest
{
    /// <summary>
    /// The publish date and time. Leave <see langword="null"/> to publish now (or keep the original date
    /// when republishing). Must not be in the future until scheduling is supported (T4.1).
    /// </summary>
    public DateTimeOffset? PublishOn { get; set; }

    /// <summary>Concurrency token; when given, a stale value fails with a conflict.</summary>
    public byte[]? RowVersion { get; set; }
}
