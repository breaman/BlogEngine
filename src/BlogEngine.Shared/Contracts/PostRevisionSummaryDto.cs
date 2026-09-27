using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One row of a post's revision history (<c>GET /api/admin/posts/{id}/revisions</c>, design 7.4, A13), without the
/// content, which <see cref="PostRevisionDto"/> carries.
/// </summary>
public sealed class PostRevisionSummaryDto
{
    /// <summary>The revision id.</summary>
    public int Id { get; set; }

    /// <summary>Why the revision was recorded: an autosave, a manual save or a publish.</summary>
    public RevisionKind Kind { get; set; }

    /// <summary>When it was recorded (UTC).</summary>
    public DateTimeOffset SavedOn { get; set; }

    /// <summary>The title at the time.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Length of the Markdown at the time, in characters, to show how much a revision changed.</summary>
    public int ContentLength { get; set; }
}