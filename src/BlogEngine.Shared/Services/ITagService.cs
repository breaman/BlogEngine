using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// Tags in the admin area (design 6.4, 7.4): autocomplete for the editor, and tag management (O3) at
/// <c>/admin/tags</c>, which renames, merges and deletes tags.
/// </summary>
/// <remarks>
/// The server implementation works on the database directly (prerendering and the <c>/api/admin/tags</c> endpoints);
/// the WebAssembly implementation calls those endpoints. Expected failures come back as a <see cref="TagResult"/>.
/// </remarks>
public interface ITagService
{
    /// <summary>Default and maximum number of autocomplete suggestions.</summary>
    const int MaxSuggestions = 10;

    /// <summary>
    /// Autocomplete: tags whose name contains <paramref name="search"/>, case-insensitively, with prefix
    /// matches first and then the most used. A blank search returns the most used tags.
    /// </summary>
    Task<IReadOnlyList<TagDto>> SearchAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>Every tag with its usage counts, alphabetically, for tag management.</summary>
    Task<IReadOnlyList<TagAdminDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag or changes its slug or description. The new name is normalized like a typed one; a name another tag
    /// already has is a <see cref="TagConflict"/> (merge instead). A changed slug redirects the old tag page and feed.
    /// </summary>
    Task<TagResult> UpdateAsync(int id, UpdateTagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges tag <paramref name="id"/> into <paramref name="targetId"/>: every post with the tag gets the target instead
    /// (posts in the trash too), the tag is deleted, and its page and feed redirect to the target's (design 6.4).
    /// </summary>
    /// <returns><see cref="TagSaved"/> with the target and its new counts.</returns>
    Task<TagResult> MergeAsync(int id, int targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tag that no post uses, not even one in the trash; otherwise the result is a <see cref="TagConflict"/>.
    /// </summary>
    Task<TagResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}