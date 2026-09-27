using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Models;

/// <summary>
/// A saved copy of a post's title and content (design 6.7). Manual and publish revisions are kept;
/// only the latest few autosaves per post are retained.
/// </summary>
public class PostRevision : FingerPrintEntityBase
{
    /// <summary>The post this revision belongs to.</summary>
    public int PostId { get; set; }

    /// <summary>The post.</summary>
    public Post Post { get; set; } = null!;

    /// <summary>Title at the time of the revision.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown content at the time of the revision.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>When the revision was recorded.</summary>
    public DateTimeOffset SavedOn { get; set; }

    /// <summary>Why the revision was recorded.</summary>
    public RevisionKind Kind { get; set; }
}