using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// The media library for the admin area (design 5.1, 7.4, 9): listing, metadata, edits and deletion.
/// </summary>
/// <remarks>
/// The server implementation works on the database and media storage directly (used while prerendering and by
/// the admin API); the WebAssembly implementation calls <c>/api/admin/media</c>. Uploads are not part of this
/// interface: the browser streams files straight to <c>POST /api/admin/media</c> so it can show progress.
/// </remarks>
public interface IMediaService
{
    /// <summary>Lists library items, newest first, with their usage counts.</summary>
    Task<PagedResult<MediaItemDto>> GetMediaAsync(MediaListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Loads one item with the posts that use it, or <see langword="null"/> if there is none.</summary>
    Task<MediaItemDto?> GetMediaItemAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the items with these public ids for rendering Markdown (the editor preview); unknown ids are
    /// simply missing from the result.
    /// </summary>
    Task<IReadOnlyList<MediaLookupItem>> LookupAsync(IReadOnlyCollection<string> publicIds, CancellationToken cancellationToken = default);

    /// <summary>Saves the alt text and caption.</summary>
    Task<MediaSaveResult> UpdateAsync(int id, MediaUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies edit operations to the original, stores the result as a new version and re-renders the posts that
    /// use the item, so they pick up the new <c>?v=</c> (design 9.2).
    /// </summary>
    Task<MediaSaveResult> EditAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes every edit (design 9.2, M7): the original becomes the current version again, under a new version number so
    /// cached copies of the edited image aren't reused, and the posts that use the item are re-rendered.
    /// </summary>
    Task<MediaSaveResult> RevertAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Save as copy" (design 9.2): applies <paramref name="operations"/> to the item's original and stores the result as
    /// a new library item with the same alt text and caption, leaving the item, and the posts that use it, untouched.
    /// </summary>
    /// <returns>The new item, or why nothing was saved.</returns>
    Task<MediaSaveResult> SaveAsCopyAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default);

    /// <summary>How many images have missing or out-of-date responsive renditions (design 9.4, T4.19).</summary>
    Task<MediaRenditionProgress> GetRenditionProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes renditions for up to <paramref name="maxItems"/> images that need them, and re-renders the posts that use
    /// those images. Call it again while <see cref="MediaRenditionProgress.Remaining"/> is above zero.
    /// </summary>
    Task<MediaRenditionProgress> GenerateRenditionsAsync(int maxItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the item and all its files. Unless <paramref name="force"/> is set, an item that posts use is
    /// kept and <see cref="MediaInUse"/> lists those posts (design 9.6).
    /// </summary>
    Task<MediaDeleteResult> DeleteAsync(int id, bool force, CancellationToken cancellationToken = default);
}
