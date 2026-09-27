namespace BlogEngine.Shared.Enums;

/// <summary>
/// Status tabs of the admin posts list (design 7.3, O2).
/// </summary>
/// <remarks>
/// Separate from <see cref="PostStatus"/> because the list filters on derived states too: <see cref="Scheduled"/>
/// is published with a future date, and <see cref="Trash"/> is soft-deleted (design 6.8). Values travel in the query
/// string by name, so they may be renamed only together with the client.
/// </remarks>
public enum PostListStatus
{
    /// <summary>Every post that isn't in the trash.</summary>
    All = 0,

    /// <summary>Drafts only.</summary>
    Draft = 1,

    /// <summary>Published posts whose publish time has passed.</summary>
    Published = 2,

    /// <summary>Published posts whose publish time is still in the future (design 6.3, A9).</summary>
    Scheduled = 3,

    /// <summary>Posts in the trash (soft-deleted), whatever their status (design 6.8, O6).</summary>
    Trash = 4
}