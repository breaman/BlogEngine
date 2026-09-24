using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A saved copy of a post's title and content (<c>GET /api/admin/posts/{id}/revisions/{revisionId}</c>, design 6.7,
/// A13), for comparing with the current post and restoring it into the editor.
/// </summary>
public sealed class PostRevisionDto
{
    /// <summary>The revision id.</summary>
    public int Id { get; set; }

    /// <summary>The post the revision belongs to.</summary>
    public int PostId { get; set; }

    /// <summary>Why the revision was recorded.</summary>
    public RevisionKind Kind { get; set; }

    /// <summary>When it was recorded (UTC).</summary>
    public DateTimeOffset SavedOn { get; set; }

    /// <summary>The title at the time.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The Markdown content at the time.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;
}
