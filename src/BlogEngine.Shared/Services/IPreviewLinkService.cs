using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// Creates, lists and revokes a post's private preview links (design 6.7, 7.4, A14), so an unpublished post can be
/// shared for feedback before it goes live.
/// </summary>
/// <remarks>
/// The server implementation works on the database directly; the WebAssembly implementation calls
/// <c>/api/admin/posts/{id}/preview-token(s)</c>. Anyone with a link can read the post until the link expires or is
/// revoked, so links are 256-bit random tokens and never listed publicly.
/// </remarks>
public interface IPreviewLinkService
{
    /// <summary>
    /// The post's links that haven't expired, newest first, or <see langword="null"/> if there is no such post outside
    /// the trash.
    /// </summary>
    Task<IReadOnlyList<PreviewLinkDto>?> GetLinksAsync(int postId, CancellationToken cancellationToken = default);

    /// <summary>Creates a link, or returns <see langword="null"/> if there is no such post outside the trash.</summary>
    /// <exception cref="FluentValidation.ValidationException">The request is invalid.</exception>
    Task<PreviewLinkDto?> CreateAsync(int postId, CreatePreviewLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revokes (deletes) a link.</summary>
    /// <returns><see langword="false"/> if the post has no such link.</returns>
    Task<bool> RevokeAsync(int postId, int linkId, CancellationToken cancellationToken = default);
}