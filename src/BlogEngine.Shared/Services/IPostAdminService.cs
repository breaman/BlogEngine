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
    /// <summary>
    /// Lists posts for the admin posts page. Posts in the trash are listed only by <see cref="Enums.PostListStatus.Trash"/>.
    /// </summary>
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

    /// <summary>
    /// Publishes the post now, schedules it for a future <see cref="PublishPostRequest.PublishOn"/>, or re-dates an
    /// already published post (design 6.3, A9).
    /// </summary>
    Task<PostSaveResult> PublishAsync(int id, PublishPostRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a published post to draft, so its public URL stops working; for a scheduled post this is
    /// <b>Unschedule</b>, which also forgets the scheduled date.
    /// </summary>
    Task<PostSaveResult> UnpublishAsync(int id, UnpublishPostRequest request, CancellationToken cancellationToken = default);

    /// <summary>Moves the post to the trash (soft delete).</summary>
    /// <returns><see langword="false"/> if there is no such post outside the trash.</returns>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a post out of the trash as a draft (design 6.8, O6). A post that was live keeps its publish date, so publishing
    /// it again restores its old URL; a scheduled post forgets its date, as with <see cref="UnpublishAsync"/>.
    /// </summary>
    /// <returns><see cref="PostSaved"/> with the restored draft, or <see cref="PostNotFound"/> if the post isn't in the trash.</returns>
    Task<PostSaveResult> RestoreAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes one post that is in the trash, with its tag links, comments, revisions, media usage and preview
    /// links (design 6.8).
    /// </summary>
    /// <returns><see langword="false"/> if there is no such post in the trash.</returns>
    Task<bool> DeletePermanentlyAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes every post in the trash, like <see cref="DeletePermanentlyAsync"/> (design 6.8, "Empty trash").</summary>
    /// <returns>The number of posts deleted.</returns>
    Task<int> EmptyTrashAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the post's revisions, newest first, without their content (design 7.4, A13), or <see langword="null"/>
    /// if there is no such post outside the trash.
    /// </summary>
    Task<IReadOnlyList<PostRevisionSummaryDto>?> GetRevisionsAsync(int postId, CancellationToken cancellationToken = default);

    /// <summary>Loads one revision of the post with its content, or <see langword="null"/> if either doesn't exist.</summary>
    Task<PostRevisionDto?> GetRevisionAsync(int postId, int revisionId, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a slug is well formed and free, and suggests the slug a save would use.</summary>
    Task<SlugCheckResult> CheckSlugAsync(SlugCheckRequest request, CancellationToken cancellationToken = default);
}