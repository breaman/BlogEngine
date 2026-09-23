using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// Creates, edits and publishes posts for the admin area (design 5.1, 7.4).
/// </summary>
/// <remarks>
/// The server implementation works on the database directly (used while prerendering and by the admin
/// API); the WebAssembly implementation calls <c>/api/admin/posts</c>. Expected failures (not found,
/// concurrency conflict, validation) come back as a <see cref="PostSaveResult"/>, not as exceptions.
/// </remarks>
public interface IPostAdminService
{
    /// <summary>Lists posts for the admin posts page (never includes the trash).</summary>
    Task<PagedResult<PostSummaryDto>> GetPostsAsync(PostListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Loads a post for editing, or <see langword="null"/> if there is no such post outside the trash.</summary>
    Task<PostEditDto?> GetPostAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a draft.</summary>
    Task<PostSaveResult> CreateAsync(PostEditDto post, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the post; for a published post the changes go live immediately (this is the editor's
    /// <b>Update</b>). Requires <see cref="PostEditDto.RowVersion"/>.
    /// </summary>
    Task<PostSaveResult> UpdateAsync(int id, PostEditDto post, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an autosave (design 10.2, Q3). A draft is updated as well; a published post is left
    /// untouched and the changes are kept as <see cref="PostEditDto.PendingChanges"/> until
    /// <see cref="UpdateAsync"/>. Requires <see cref="PostEditDto.RowVersion"/>.
    /// </summary>
    Task<PostSaveResult> AutosaveAsync(int id, PostEditDto post, CancellationToken cancellationToken = default);

    /// <summary>Publishes the post now, or re-dates an already published post (design 6.3).</summary>
    Task<PostSaveResult> PublishAsync(int id, PublishPostRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns a published post to draft, so its public URL stops working.</summary>
    Task<PostSaveResult> UnpublishAsync(int id, UnpublishPostRequest request, CancellationToken cancellationToken = default);

    /// <summary>Moves the post to the trash (soft delete).</summary>
    /// <returns><see langword="false"/> if there is no such post outside the trash.</returns>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a slug is well formed and free, and suggests the slug a save would use.</summary>
    Task<SlugCheckResult> CheckSlugAsync(SlugCheckRequest request, CancellationToken cancellationToken = default);
}
