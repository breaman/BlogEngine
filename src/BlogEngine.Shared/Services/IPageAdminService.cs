using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// Admin operations on standalone pages (design 6.7, 7.3, A17), implemented twice: on the server over the database, and
/// in the WebAssembly client over the <c>/api/admin/pages</c> endpoints.
/// </summary>
/// <remarks>
/// Pages are simpler than posts: no dates, tags, scheduling, autosave or revisions. Saving a published page updates it
/// straight away, and deleting one removes it for good (pages have no trash).
/// </remarks>
public interface IPageAdminService
{
    /// <summary>Every page, drafts included, in navigation order and then by title.</summary>
    Task<IReadOnlyList<PageSummaryDto>> GetPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>One page for the editor, or <see langword="null"/> if it doesn't exist.</summary>
    Task<PageEditDto?> GetPageAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a draft page.</summary>
    Task<PageSaveResult> CreateAsync(PageEditDto page, CancellationToken cancellationToken = default);

    /// <summary>Saves a page; a published page's changes are live at once.</summary>
    Task<PageSaveResult> UpdateAsync(int id, PageEditDto page, CancellationToken cancellationToken = default);

    /// <summary>Publishes a page at <c>/{slug}</c>.</summary>
    Task<PageSaveResult> PublishAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns a page to draft; its URL answers 404 and it leaves the navigation.</summary>
    Task<PageSaveResult> UnpublishAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a page permanently; <see langword="false"/> if it didn't exist.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}