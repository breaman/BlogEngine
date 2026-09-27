namespace BlogEngine.Shared.Contracts;

/// <summary>
/// An autosaved edit of a published post that isn't live yet (design 10.2, Q3). Autosave never changes
/// published content; the editor offers these changes and <b>Update</b> makes them live.
/// </summary>
public sealed class PostPendingChangesDto
{
    /// <summary>The autosaved title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The autosaved Markdown content.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>When the autosave was recorded (UTC).</summary>
    public DateTimeOffset SavedOn { get; set; }
}