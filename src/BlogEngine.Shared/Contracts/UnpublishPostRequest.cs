namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Body of <c>POST /api/admin/posts/{id}/unpublish</c> (design 6.3, 7.4). For a scheduled post this is
/// <b>Unschedule</b>: it returns to draft and forgets the scheduled date.
/// </summary>
public sealed class UnpublishPostRequest
{
    /// <summary>Concurrency token; when given, a stale value fails with a conflict.</summary>
    public byte[]? RowVersion { get; set; }
}
