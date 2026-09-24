using System.Globalization;

using BlogEngine.Data.Models;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// The read side of standalone pages (design 6.7, 7.1, 11, A17): the published pages for the navigation and the
/// sitemap, and one page's content for <c>/{slug}</c>, cached in <see cref="HybridCache"/> with the tag
/// <see cref="PublicCacheTags.Pages"/> until <see cref="CacheInvalidator.PagesChangedAsync"/> evicts them.
/// </summary>
/// <remarks>
/// Built like <see cref="PublicPostQueries"/>: one snapshot of every published page (a handful at most), content cached
/// per page id and only for slugs found in the snapshot, so arbitrary URLs can't grow the cache; keys carry
/// <see cref="CacheInvalidator.PagesGeneration"/>, and loads run in their own scope, so this is a singleton. Pages have
/// no publish date, so unlike posts nothing depends on the clock.
/// </remarks>
public sealed class PublicPageQueries(HybridCache cache, CacheInvalidator invalidator, IServiceScopeFactory scopeFactory)
{
    /// <summary>Safety net for entries nothing evicts.</summary>
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(10)
    };

    private static readonly string[] Tags = [PublicCacheTags.Pages];

    /// <summary>Every published page, in navigation order.</summary>
    public async Task<PublicPageIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(Key("index"), LoadIndexAsync, EntryOptions, Tags, cancellationToken);
    }

    /// <summary>
    /// The published page with this slug and its content, or <see langword="null"/>. The caller compares its
    /// <see cref="PublicPageSummary.Path"/> with the request to redirect other spellings to the canonical URL.
    /// </summary>
    public async Task<PublicPageContent?> GetPageAsync(string slug, CancellationToken cancellationToken = default)
    {
        if ((await GetIndexAsync(cancellationToken)).Find(slug) is not { } summary)
        {
            return null;
        }

        return await cache.GetOrCreateAsync(Key("page", summary.Id), (Queries: this, Summary: summary),
            static (state, ct) => state.Queries.LoadContentAsync(state.Summary, ct),
            EntryOptions, Tags, cancellationToken);
    }

    /// <summary>The cache key of <paramref name="name"/> in the current generation, such as <c>public-pages:3:page:7</c>.</summary>
    private string Key(string name, int? id = null)
    {
        return id is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"public-pages:{invalidator.PagesGeneration}:{name}:{value}")
            : string.Create(CultureInfo.InvariantCulture, $"public-pages:{invalidator.PagesGeneration}:{name}");
    }

    /// <summary>Loads the summaries of the published pages.</summary>
    private async ValueTask<PublicPageIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pages = await dbContext.Pages
            .AsNoTracking()
            .Where(p => p.Status == PostStatus.Published && p.Slug != string.Empty)
            .OrderBy(p => p.NavOrder)
            .ThenBy(p => p.Title)
            .Select(p => new PublicPageSummary(p.Id, p.Title, p.Slug, p.ShowInNav, p.NavOrder, p.ModifiedOn))
            .ToListAsync(cancellationToken);

        return pages.Count == 0 ? PublicPageIndex.Empty : new PublicPageIndex(pages);
    }

    /// <summary>Loads a published page's content, resolving in-page links and building its table of contents.</summary>
    private async ValueTask<PublicPageContent?> LoadContentAsync(PublicPageSummary summary, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await dbContext.Pages
            .AsNoTracking()
            .Where(p => p.Id == summary.Id && p.Status == PostStatus.Published)
            .Select(p => new { p.ContentHtml, p.HasCodeBlocks, p.Summary, p.MetaTitle, p.MetaDescription })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PublicPageContent(summary, FragmentLinks.Resolve(row.ContentHtml, summary.Path), row.HasCodeBlocks,
                row.Summary, row.MetaTitle, row.MetaDescription, PostOutline.FromHtml(row.ContentHtml));
    }
}
