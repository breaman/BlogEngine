namespace BlogEngine.Shared.Enums;

/// <summary>
/// Status tabs of the admin posts list (design 7.3, O2).
/// </summary>
/// <remarks>
/// Separate from <see cref="PostStatus"/> because the list filters on derived states too: Phase 4 adds
/// <c>Scheduled</c> (published with a future date) and <c>Trash</c> (soft-deleted). Values travel in the
/// query string by name, so they may be renamed only together with the client.
/// </remarks>
public enum PostListStatus
{
    /// <summary>Every post that isn't in the trash.</summary>
    All = 0,

    /// <summary>Drafts only.</summary>
    Draft = 1,

    /// <summary>Published posts only.</summary>
    Published = 2
}
