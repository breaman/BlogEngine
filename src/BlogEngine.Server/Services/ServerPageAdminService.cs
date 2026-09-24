using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="IPageAdminService"/> (design 6.7, A17), working on
/// <see cref="ApplicationDbContext"/> directly. Used while prerendering the admin pages and behind
/// <c>/api/admin/pages</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every save renders the Markdown with the same pipeline, media lookup and sanitizer as posts
/// (<see cref="PostContentRenderer"/>), fills a blank summary, and resolves the slug: a blank slug is generated from
/// the title and made unique (<c>-2</c>, <c>-3</c>, …), skipping <see cref="ReservedSlugs"/>; a typed slug that another
/// page uses is a validation error rather than silently changed. Renaming a published page records a redirect from
/// its old URL, as for posts (P16).
/// </para>
/// <para>
/// Once a change that readers can see has committed, <see cref="CacheInvalidator.PagesChangedAsync"/> evicts the page
/// caches, so the navigation, the page and the sitemap update on the next request.
/// </para>
/// <para>
/// Library images in a page render with their size and version, but pages don't record media usage the way posts do
/// (<see cref="PostMedia"/>), so the media library doesn't count them as uses of an image.
/// </para>
/// </remarks>
public sealed class ServerPageAdminService(
    ApplicationDbContext dbContext,
    PostContentRenderer contentRenderer,
    IValidator<PageEditDto> validator,
    CacheInvalidator cacheInvalidator) : IPageAdminService
{
    /// <summary>Slug used when a title has no characters a slug can use.</summary>
    private const string FallbackSlug = "page";

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageSummaryDto>> GetPagesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Pages
            .AsNoTracking()
            .OrderBy(p => p.NavOrder)
            .ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.Slug, p.Status, p.ShowInNav, p.NavOrder, p.ModifiedOn })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new PageSummaryDto
            {
                Id = r.Id,
                Title = r.Title,
                Slug = r.Slug,
                Status = r.Status,
                ShowInNav = r.ShowInNav,
                NavOrder = r.NavOrder,
                ModifiedOn = r.ModifiedOn,
                PublicPath = PublicPathOf(r.Status, r.Slug)
            })
        ];
    }

    /// <inheritdoc />
    public async Task<PageEditDto?> GetPageAsync(int id, CancellationToken cancellationToken = default)
    {
        var page = await dbContext.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return page is null ? null : ToEditDto(page);
    }

    /// <inheritdoc />
    public async Task<PageSaveResult> CreateAsync(PageEditDto page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (Validate(page) is { } invalid)
        {
            return invalid;
        }

        var entity = new Page { Status = PostStatus.Draft };
        if (await ApplyEditsAsync(entity, page, cancellationToken) is { } slugTaken)
        {
            return slugTaken;
        }

        dbContext.Pages.Add(entity);
        if (await TrySaveAsync(cancellationToken) is { } lostRace)
        {
            return lostRace;
        }

        return new PageSaved(ToEditDto(entity));
    }

    /// <inheritdoc />
    public async Task<PageSaveResult> UpdateAsync(int id, PageEditDto page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (Validate(page) is { } invalid)
        {
            return invalid;
        }

        var entity = await dbContext.Pages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return PageSaveResult.NotFound;
        }

        var oldPath = PublicPathOf(entity.Status, entity.Slug);
        if (await ApplyEditsAsync(entity, page, cancellationToken) is { } slugTaken)
        {
            return slugTaken;
        }

        if (oldPath is not null && PublicPathOf(entity.Status, entity.Slug) is { } newPath && newPath != oldPath)
        {
            await RedirectWriter.AddAsync(dbContext, oldPath, newPath, cancellationToken);
        }

        if (await TrySaveAsync(cancellationToken) is { } lostRace)
        {
            return lostRace;
        }

        if (entity.Status == PostStatus.Published)
        {
            await cacheInvalidator.PagesChangedAsync(entity.Id);
        }

        return new PageSaved(ToEditDto(entity));
    }

    /// <inheritdoc />
    public Task<PageSaveResult> PublishAsync(int id, CancellationToken cancellationToken = default)
    {
        return SetStatusAsync(id, PostStatus.Published, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PageSaveResult> UnpublishAsync(int id, CancellationToken cancellationToken = default)
    {
        return SetStatusAsync(id, PostStatus.Draft, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Pages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        // Pages have no trash to restore from, so a delete is permanent and frees the slug. SoftDeleteInterceptor lets a
        // row through as a real delete when it was already in the trash, which marking the original value says.
        var wasPublished = entity.Status == PostStatus.Published;
        dbContext.Entry(entity).Property(p => p.IsDeleted).OriginalValue = true;
        dbContext.Pages.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasPublished)
        {
            await cacheInvalidator.PagesChangedAsync(id);
        }

        return true;
    }

    /// <summary>
    /// Saves, turning a lost race for the slug's unique index (another save claimed it a moment earlier) into a validation
    /// error on the slug rather than a server error.
    /// </summary>
    private async Task<PageInvalid?> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            dbContext.ChangeTracker.Clear();
            return new PageInvalid(new Dictionary<string, string[]>
            {
                [nameof(PageEditDto.Slug)] = ["Another page took this slug while you were saving. Save again to pick another."]
            });
        }
    }

    /// <summary>Publishes or unpublishes a page, evicting the caches when its status changed.</summary>
    private async Task<PageSaveResult> SetStatusAsync(int id, PostStatus status, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Pages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return PageSaveResult.NotFound;
        }

        if (entity.Status != status)
        {
            entity.Status = status;
            await dbContext.SaveChangesAsync(cancellationToken);
            await cacheInvalidator.PagesChangedAsync(entity.Id);
        }

        return new PageSaved(ToEditDto(entity));
    }

    /// <summary>
    /// Copies the editable fields onto <paramref name="page"/> and recomputes the rendered HTML, summary and slug.
    /// Returns a validation failure, and changes nothing, when a typed slug belongs to another page.
    /// </summary>
    private async Task<PageInvalid?> ApplyEditsAsync(Page page, PageEditDto dto, CancellationToken cancellationToken)
    {
        var title = dto.Title.Trim();
        var markdown = dto.ContentMarkdown ?? string.Empty;
        var requestedSlug = dto.Slug?.Trim();

        string slug;
        if (string.IsNullOrEmpty(requestedSlug))
        {
            // A published page keeps its URL; otherwise the slug follows the title.
            slug = page.Status == PostStatus.Published && page.Slug.Length > 0
                ? page.Slug
                : await UniqueSlugAsync(SlugFromTitle(title), page.Id, cancellationToken);
        }
        else if (requestedSlug == page.Slug || !await IsSlugTakenAsync(requestedSlug, page.Id, cancellationToken))
        {
            slug = requestedSlug;
        }
        else
        {
            return new PageInvalid(new Dictionary<string, string[]>
            {
                [nameof(PageEditDto.Slug)] = [$"Another page already uses '{requestedSlug}'."]
            });
        }

        var rendered = await contentRenderer.RenderAsync(markdown, cancellationToken);

        page.Title = title;
        page.Slug = slug;
        page.ContentMarkdown = markdown;
        page.ContentHtml = rendered.Html;
        page.HasCodeBlocks = rendered.ContainsCodeBlocks;
        page.Summary = string.IsNullOrWhiteSpace(dto.Summary) ? SummaryGenerator.FromMarkdown(markdown) : dto.Summary.Trim();
        page.ShowInNav = dto.ShowInNav;
        page.NavOrder = dto.NavOrder;
        page.MetaTitle = NullIfBlank(dto.MetaTitle);
        page.MetaDescription = NullIfBlank(dto.MetaDescription);

        return null;
    }

    /// <summary>
    /// <paramref name="slug"/>, or the first of <c>slug-2</c>, <c>slug-3</c>, … that no other page uses and that isn't
    /// reserved (a page titled "Search" gets <c>search-2</c>).
    /// </summary>
    private async Task<string> UniqueSlugAsync(string slug, int pageId, CancellationToken cancellationToken)
    {
        var prefix = slug + "-";
        var taken = (await dbContext.Pages
                .IgnoreQueryFilters([QueryFilters.SoftDelete])
                .Where(p => p.Id != pageId && (p.Slug == slug || p.Slug.StartsWith(prefix)))
                .Select(p => p.Slug)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return SlugGenerator.MakeUnique(slug, s => taken.Contains(s) || ReservedSlugs.IsReserved(s));
    }

    /// <summary>
    /// Whether another page uses <paramref name="slug"/>. Soft-deleted rows count, because the unique index covers them
    /// (only pages deleted before deletes became permanent can be in that state).
    /// </summary>
    private Task<bool> IsSlugTakenAsync(string slug, int pageId, CancellationToken cancellationToken)
    {
        return dbContext.Pages
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AnyAsync(p => p.Id != pageId && p.Slug == slug, cancellationToken);
    }

    /// <summary>Validates the DTO with the shared validator, returning <see langword="null"/> when it is valid.</summary>
    private PageInvalid? Validate(PageEditDto page)
    {
        var errors = validator.Validate(page).ToDictionary();
        return errors.Count == 0 ? null : new PageInvalid(errors.AsReadOnly());
    }

    /// <summary>The editor DTO of a page.</summary>
    private static PageEditDto ToEditDto(Page page)
    {
        return new PageEditDto
        {
            Id = page.Id,
            Title = page.Title,
            Slug = page.Slug,
            Summary = page.Summary,
            ContentMarkdown = page.ContentMarkdown,
            ShowInNav = page.ShowInNav,
            NavOrder = page.NavOrder,
            MetaTitle = page.MetaTitle,
            MetaDescription = page.MetaDescription,
            Status = page.Status,
            ModifiedOn = page.ModifiedOn,
            PublicPath = PublicPathOf(page.Status, page.Slug)
        };
    }

    /// <summary>The page's public path while it is published, otherwise <see langword="null"/>.</summary>
    private static string? PublicPathOf(PostStatus status, string slug)
    {
        return status == PostStatus.Published && slug.Length > 0 ? SitePaths.Page(slug) : null;
    }

    /// <summary>The slug generated from a title, with a fallback for titles that yield nothing.</summary>
    private static string SlugFromTitle(string title)
    {
        var slug = SlugGenerator.Generate(title);
        return slug.Length > 0 ? slug : FallbackSlug;
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
