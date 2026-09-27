namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Body of <c>POST /api/admin/posts/{id}/publish</c> (design 6.3, 7.4).
/// </summary>
public sealed class PublishPostRequest
{
    /// <summary>
    /// The publish date and time. A future value schedules the post (design 6.3, A9): it stays hidden until then.
    /// Leave <see langword="null"/> to publish now, or to keep the original date when republishing a post that was
    /// live before; a scheduled post published with <see langword="null"/> goes live now.
    /// </summary>
    public DateTimeOffset? PublishOn { get; set; }

    /// <summary>Concurrency token; when given, a stale value fails with a conflict.</summary>
    public byte[]? RowVersion { get; set; }
}